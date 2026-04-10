using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Services;

/// <summary>
/// Paragraph-addressable tool loop: read_section, propose_patch, finish (JSON only).
/// </summary>
public static class AgenticEditLoop
{
    private const int FullDraftCharBudget = 60_000;
    private const int SummaryPrefixChars = 400;
    private const int MaxReplacementChars = 80_000;
    /// <summary>Max chars of raw JSON sent on SignalR as llmPreview (full response is always persisted on the generation run LLM call log).</summary>
    private const int ProgressLlmPreviewMaxChars = 500_000;
    private const int ProgressPatchExcerptChars = 7000;

    private static readonly string SystemPrompt = """
        You are a fiction editor agent refining a scene draft. You improve clarity, pacing, and alignment with instructions using tools.
        Respond with a single JSON object only (no markdown fences). Property names are case-insensitive.

        Tools:
        - { "action": "read_section", "paragraphStart": <int>, "paragraphEnd": <int> } — inclusive paragraph indices (0..N-1). Use to read full text when the draft view is summarized.
        - { "action": "propose_patch", "paragraphStart": <int>, "paragraphEnd": <int>, "replacement": "<prose>", "reason": "<why this edit>" } — replace inclusive range with new prose. Include "reason" (one or two sentences: what you are improving and why). Replacement may use blank lines to split into multiple paragraphs.
        - { "action": "finish", "reason": "<short reason>" } — the draft is acceptable; stop editing.

        Rules:
        - Prefer small targeted patches; avoid rewriting the whole scene unless necessary.
        - Preserve continuity, voice, and facts implied by scene instructions and world context.
        - CONTENT PRESERVATION (non-negotiable): Every propose_patch replacement MUST keep all plot-critical substance from the replaced paragraphs—reveals, twists, decisions, stakes, foreshadowing, on-page events, dialogue commitments, and causal links. Do not "tighten" or "streamline" by deleting story beats, compressing scenes into summary, or replacing dramatized moments with abstract narration. If you cannot improve wording without losing any of that substance, use finish instead.
        - Patches are line edits: same story, clearer or more vivid prose. Never substitute a shorter synopsis of events for the original prose.
        - Show, don't tell: patches should add or refine concrete action, dialogue, and sensory detail; avoid replacing dramatized beats with abstract emotional labels or explanatory narration unless the author instruction requires it.
        - Reference variety: do not lean on repeating characters' full names every time they appear. Mix in relationship to the viewpoint character (e.g. "her brother", "the detective"), role or attitude from the POV ("the woman who'd lied to him"), physical or situational anchors ("the man at the bar"), and occasional name use for clarity—especially on first introduction or when many people are in the scene.
        - HARD CONSTRAINT: Do not introduce named characters, relationships, or plot events not already allowed by the scene synopsis/instructions and the Linked world-building / relationship text in the user message. Do not invent story beats to "improve" the scene.
        - Summarized draft view: you only see short previews per paragraph. You MUST call read_section on a prior turn for an inclusive range that fully covers any paragraph range before you propose_patch that same range (the server enforces this). Do not patch blind.
        - Indices always refer to the CURRENT draft shown in the message (after any prior patches in this session).
        """;

    public static async Task<string> RunAsync(
        string initialDraft,
        string sceneInstructions,
        string? expectedEndNotes,
        string worldBlock,
        int maxTurns,
        ILogger logger,
        Func<string, string, CancellationToken, Task<(string messageText, string raw)>> chatJsonAsync,
        IGenerationProgressNotifier notifier,
        Guid runId,
        Func<long> pipelineElapsedMs,
        CancellationToken cancellationToken)
    {
        var paragraphs = SplitParagraphs(initialDraft);
        if (paragraphs.Count == 0)
            return initialDraft;

        var readRanges = new List<(int start, int end)>();
        string? lastToolResult = null;
        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var numbered = BuildParagraphReference(paragraphs);
            var paragraphingWarning = BuildParagraphingWarning(paragraphs);
            var user = BuildUserMessage(
                sceneInstructions,
                expectedEndNotes,
                worldBlock,
                turn,
                maxTurns,
                paragraphs.Count,
                numbered,
                lastToolResult,
                paragraphingWarning);

            var turnSw = Stopwatch.StartNew();
            var (raw, _) = await chatJsonAsync(SystemPrompt, user, cancellationToken);
            turnSw.Stop();
            var cleaned = LlmJson.StripMarkdownFences(raw).Trim();
            AgentEditActionDto? action;
            try
            {
                action = LlmJson.Deserialize<AgentEditActionDto>(cleaned);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agentic edit turn {Turn}: invalid JSON", turn);
                lastToolResult = $"Error: output was not valid JSON. Fix and try again. Raw (truncated): {Truncate(cleaned, ProgressLlmPreviewMaxChars)}";
                await notifier.NotifyAsync(runId, "AgentEditTurn", nameof(PipelineStep.AgentEdit),
                    $"Turn {turn}/{maxTurns}: model returned invalid JSON; the editor will retry. ({turnSw.ElapsedMilliseconds} ms for LLM)",
                    cancellationToken,
                    pipelineElapsedMs(),
                    turnSw.ElapsedMilliseconds,
                    Truncate(cleaned, ProgressLlmPreviewMaxChars),
                    SerializeAgentLlmPrompt(SystemPrompt, user));
                continue;
            }

            if (action is null || string.IsNullOrWhiteSpace(action.Action))
            {
                lastToolResult = "Error: missing \"action\" in JSON.";
                await notifier.NotifyAsync(runId, "AgentEditTurn", nameof(PipelineStep.AgentEdit),
                    $"Turn {turn}/{maxTurns}: JSON missing \"action\" field. ({turnSw.ElapsedMilliseconds} ms)",
                    cancellationToken,
                    pipelineElapsedMs(),
                    turnSw.ElapsedMilliseconds,
                    Truncate(cleaned, ProgressLlmPreviewMaxChars),
                    SerializeAgentLlmPrompt(SystemPrompt, user));
                continue;
            }

            var kind = action.Action.Trim().ToLowerInvariant();
            var llmPreview = Truncate(cleaned, ProgressLlmPreviewMaxChars);
            var llmRequest = SerializeAgentLlmPrompt(SystemPrompt, user);

            switch (kind)
            {
                case "finish":
                    logger.LogInformation("Agentic edit finished at turn {Turn}: {Reason}", turn, action.Reason ?? "");
                    await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                        turnSw.ElapsedMilliseconds,
                        $"Editor finished (agent pass). Why: {Truncate(action.Reason ?? "(no reason)", 400)}",
                        llmPreview, llmRequest);
                    return JoinParagraphs(paragraphs);

                case "read_section":
                {
                    if (action.ParagraphStart is not { } rs || action.ParagraphEnd is not { } re)
                    {
                        lastToolResult = "Error: read_section requires paragraphStart and paragraphEnd (inclusive).";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"read_section failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    if (rs < 0 || re < rs || re >= paragraphs.Count)
                    {
                        lastToolResult =
                            $"Error: invalid range {rs}..{re} for draft with {paragraphs.Count} paragraphs (valid indices 0..{paragraphs.Count - 1}).";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"read_section failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    var slice = paragraphs.Skip(rs).Take(re - rs + 1).ToList();
                    var body = JoinParagraphs(slice);
                    readRanges.Add((rs, re));
                    lastToolResult = $"read_section result (paragraphs {rs}..{re}):\n{body}";
                    await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                        turnSw.ElapsedMilliseconds,
                        $"read_section — requested full text for paragraphs {rs}..{re} (inclusive).\nWhy: inspect or prepare before editing.\n\n{Truncate(body, 12_000)}",
                        llmPreview, llmRequest);
                    break;
                }

                case "propose_patch":
                {
                    if (action.ParagraphStart is not { } ps || action.ParagraphEnd is not { } pe)
                    {
                        lastToolResult = "Error: propose_patch requires paragraphStart and paragraphEnd (inclusive).";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    if (ps < 0 || pe < ps || pe >= paragraphs.Count)
                    {
                        lastToolResult =
                            $"Error: invalid range {ps}..{pe} for draft with {paragraphs.Count} paragraphs (valid indices 0..{paragraphs.Count - 1}).";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    if (ShouldUseSummary(paragraphs) && !IsRangeCoveredByReads(ps, pe, readRanges))
                    {
                        lastToolResult =
                            $"Error: summarized draft view only shows previews. On a prior turn, call read_section with a range that fully covers {ps}..{pe} (inclusive) before propose_patch. Then patch the same indices.";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    var replacement = action.Replacement ?? "";
                    if (string.IsNullOrWhiteSpace(replacement))
                    {
                        lastToolResult = "Error: propose_patch requires non-empty replacement.";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    if (replacement.Length > MaxReplacementChars)
                    {
                        lastToolResult =
                            $"Error: replacement exceeds {MaxReplacementChars} characters.";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    var originalSpan = JoinParagraphs(paragraphs.Skip(ps).Take(pe - ps + 1).ToList());
                    var originalWords = CountWords(originalSpan);
                    var replacementWords = CountWords(replacement);
                    if (originalWords >= 50 && replacementWords < originalWords * 0.55)
                    {
                        logger.LogWarning(
                            "Agentic edit: rejected patch for excessive shortening ({OriginalWords} -> {ReplacementWords} words)",
                            originalWords, replacementWords);
                        lastToolResult =
                            $"Error: replacement is much shorter than the replaced span (~{originalWords} words vs ~{replacementWords}). You must preserve plot and on-page events—tighten wording without deleting beats, or use finish.";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch rejected — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    try
                    {
                        ApplyPatch(paragraphs, ps, pe, replacement);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Agentic edit patch failed");
                        lastToolResult = $"Error applying patch: {ex.Message}";
                        await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                            turnSw.ElapsedMilliseconds, $"propose_patch failed — {lastToolResult}", llmPreview, llmRequest);
                        break;
                    }

                    readRanges.Clear();
                    lastToolResult =
                        $"propose_patch applied: replaced paragraphs {ps}..{pe}. Draft now has {paragraphs.Count} paragraphs.";
                    var patchDetail = BuildProposePatchProgressDetail(ps, pe, action.Reason, originalSpan, replacement);
                    await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                        turnSw.ElapsedMilliseconds, patchDetail, llmPreview, llmRequest);
                    break;
                }

                default:
                    lastToolResult = $"Error: unknown action \"{action.Action}\". Use read_section, propose_patch, or finish.";
                    await NotifyAgentEditProgressAsync(notifier, runId, pipelineElapsedMs, cancellationToken, turn, maxTurns,
                        turnSw.ElapsedMilliseconds, lastToolResult, llmPreview, llmRequest);
                    break;
            }
        }

        logger.LogWarning("Agentic edit stopped after {MaxTurns} turns without finish", maxTurns);
        return JoinParagraphs(paragraphs);
    }

    public static List<string> SplitParagraphs(string text)
    {
        var t = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = t.Split(new[] { "\n\n" }, StringSplitOptions.None);
        var list = new List<string>();
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length > 0)
                list.Add(s);
        }

        if (list.Count == 0 && !string.IsNullOrWhiteSpace(text))
            list.Add(text.Trim());
        return list;
    }

    public static string JoinParagraphs(IReadOnlyList<string> paragraphs) =>
        string.Join("\n\n", paragraphs);

    internal static void ApplyPatch(List<string> paragraphs, int start, int endInclusive, string replacement)
    {
        var newParas = SplitParagraphs(replacement);
        if (newParas.Count == 0)
            throw new InvalidOperationException("Replacement produced no paragraphs.");

        var removeCount = endInclusive - start + 1;
        if (start < 0 || endInclusive >= paragraphs.Count || removeCount <= 0)
            throw new InvalidOperationException("Invalid paragraph range.");

        paragraphs.RemoveRange(start, removeCount);
        paragraphs.InsertRange(start, newParas);
    }

    private static string BuildParagraphReference(IReadOnlyList<string> paragraphs)
    {
        if (ShouldUseSummary(paragraphs))
            return BuildSummaryReference(paragraphs);

        var sb = new StringBuilder();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            sb.Append('[').Append(i).Append("]\n");
            sb.Append(paragraphs[i]);
            if (i < paragraphs.Count - 1)
                sb.Append("\n\n");
        }

        return sb.ToString();
    }

    private static bool ShouldUseSummary(IReadOnlyList<string> paragraphs)
    {
        var n = 0;
        foreach (var p in paragraphs)
            n += p.Length;
        return n > FullDraftCharBudget;
    }

    private static string BuildSummaryReference(IReadOnlyList<string> paragraphs)
    {
        var sb = new StringBuilder();
        sb.Append("(Summarized: draft is long; use read_section for full paragraph text.)\n\n");
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var p = paragraphs[i];
            var preview = p.Length <= SummaryPrefixChars ? p : p[..SummaryPrefixChars].TrimEnd() + "…";
            var words = p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            sb.Append('[').Append(i).Append("] (").Append(words).Append(" words) ").AppendLine(preview);
        }

        return sb.ToString();
    }

    private static string BuildUserMessage(
        string sceneInstructions,
        string? expectedEndNotes,
        string worldBlock,
        int turn,
        int maxTurns,
        int paragraphCount,
        string numberedReference,
        string? lastToolResult,
        string? paragraphingWarning)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Turn {turn} of {maxTurns} (draft has {paragraphCount} paragraphs, indices 0..{Math.Max(0, paragraphCount - 1)}).");
        sb.AppendLine();
        sb.AppendLine("Scene instructions:");
        sb.AppendLine(sceneInstructions);
        sb.AppendLine();
        sb.AppendLine("Expected end notes (if any):");
        sb.AppendLine(string.IsNullOrEmpty(expectedEndNotes) ? "(none)" : expectedEndNotes);
        sb.AppendLine();
        sb.AppendLine("World context:");
        sb.AppendLine(worldBlock);
        sb.AppendLine();
        if (!string.IsNullOrEmpty(lastToolResult))
        {
            sb.AppendLine("Last tool result:");
            sb.AppendLine(lastToolResult);
            sb.AppendLine();
        }

        if (!string.IsNullOrEmpty(paragraphingWarning))
        {
            sb.AppendLine(paragraphingWarning);
            sb.AppendLine();
        }

        sb.AppendLine("Current draft (paragraph-index reference):");
        sb.AppendLine(numberedReference);
        return sb.ToString();
    }

    private static string? BuildParagraphingWarning(IReadOnlyList<string> paragraphs)
    {
        if (paragraphs.Count != 1)
            return null;
        if (paragraphs[0].Length <= 1500)
            return null;
        return """
            INDEXING NOTE: The draft is ONE paragraph [0] (no blank lines between blocks). Replacing [0,0] replaces the ENTIRE scene.
            If you only mean to edit part of the text, you still must replace [0,0] but your replacement must include ALL original plot and events—never a shortened version.
            """;
    }

    private static bool IsRangeCoveredByReads(int patchStart, int patchEnd, List<(int start, int end)> reads)
    {
        foreach (var (rs, re) in reads)
        {
            if (rs <= patchStart && re >= patchEnd)
                return true;
        }

        return false;
    }

    private static int CountWords(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return 0;
        return s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static async Task NotifyAgentEditProgressAsync(
        IGenerationProgressNotifier notifier,
        Guid runId,
        Func<long> pipelineElapsedMs,
        CancellationToken cancellationToken,
        int turn,
        int maxTurns,
        long turnMs,
        string detailBody,
        string? llmPreview,
        string? llmRequest)
    {
        var detail = $"Turn {turn}/{maxTurns}: {detailBody}";
        await notifier.NotifyAsync(runId, "AgentEditTurn", nameof(PipelineStep.AgentEdit),
            detail, cancellationToken, pipelineElapsedMs(), turnMs, llmPreview, llmRequest);
    }

    private static string BuildProposePatchProgressDetail(int ps, int pe, string? reason, string originalSpan, string replacement)
    {
        var sb = new StringBuilder();
        sb.AppendLine("propose_patch — change applied.");
        sb.AppendLine($"Requested: replace paragraphs {ps}..{pe} (inclusive).");
        sb.AppendLine(
            $"Why: {(string.IsNullOrWhiteSpace(reason) ? "(author model did not provide \"reason\")" : reason.Trim())}");
        sb.AppendLine("Previous text:");
        sb.AppendLine(Truncate(originalSpan, ProgressPatchExcerptChars));
        sb.AppendLine("New text:");
        sb.AppendLine(Truncate(replacement, ProgressPatchExcerptChars));
        return sb.ToString();
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private static string SerializeAgentLlmPrompt(string system, string user) =>
        JsonSerializer.Serialize(new { system, user }, new JsonSerializerOptions { WriteIndented = true });
}

internal sealed class AgentEditActionDto
{
    public string Action { get; set; } = "";
    public int? ParagraphStart { get; set; }
    public int? ParagraphEnd { get; set; }
    public string? Replacement { get; set; }
    public string? Reason { get; set; }
}
