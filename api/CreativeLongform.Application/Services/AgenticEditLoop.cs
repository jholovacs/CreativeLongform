using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    private const int SummaryPrefixChars = 140;
    private const int MaxReplacementChars = 80_000;

    private static readonly string SystemPrompt = """
        You are a fiction editor agent refining a scene draft. You improve clarity, pacing, and alignment with instructions using tools.
        Respond with a single JSON object only (no markdown fences). Property names are case-insensitive.

        Tools:
        - { "action": "read_section", "paragraphStart": <int>, "paragraphEnd": <int> } — inclusive paragraph indices (0..N-1). Use to read full text when the draft view is summarized.
        - { "action": "propose_patch", "paragraphStart": <int>, "paragraphEnd": <int>, "replacement": "<prose>" } — replace inclusive range with new prose. Replacement may use blank lines to split into multiple paragraphs.
        - { "action": "finish", "reason": "<short reason>" } — the draft is acceptable; stop editing.

        Rules:
        - Prefer small targeted patches; avoid rewriting the whole scene unless necessary.
        - Preserve continuity, voice, and facts implied by scene instructions and world context.
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

        string? lastToolResult = null;
        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var numbered = BuildParagraphReference(paragraphs);
            var user = BuildUserMessage(
                sceneInstructions,
                expectedEndNotes,
                worldBlock,
                turn,
                maxTurns,
                paragraphs.Count,
                numbered,
                lastToolResult);

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
                lastToolResult = $"Error: output was not valid JSON. Fix and try again. Raw (truncated): {Truncate(cleaned, 400)}";
                await notifier.NotifyAsync(runId, "AgentEditTurn", nameof(PipelineStep.AgentEdit),
                    $"Turn {turn}/{maxTurns}: model returned invalid JSON; the editor will retry. ({turnSw.ElapsedMilliseconds} ms for LLM)",
                    cancellationToken,
                    pipelineElapsedMs(),
                    turnSw.ElapsedMilliseconds,
                    Truncate(cleaned, 400),
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
                    Truncate(cleaned, 400),
                    SerializeAgentLlmPrompt(SystemPrompt, user));
                continue;
            }

            var kind = action.Action.Trim().ToLowerInvariant();
            var detail = kind == "finish"
                ? $"Turn {turn}/{maxTurns}: editor finished — {Truncate(action.Reason ?? "(no reason)", 200)} (LLM {turnSw.ElapsedMilliseconds} ms)"
                : $"Turn {turn}/{maxTurns}: tool action «{kind}» (LLM {turnSw.ElapsedMilliseconds} ms)";
            await notifier.NotifyAsync(runId, "AgentEditTurn", nameof(PipelineStep.AgentEdit), detail, cancellationToken,
                pipelineElapsedMs(), turnSw.ElapsedMilliseconds, Truncate(cleaned, 400),
                SerializeAgentLlmPrompt(SystemPrompt, user));

            switch (kind)
            {
                case "finish":
                    logger.LogInformation("Agentic edit finished at turn {Turn}: {Reason}", turn, action.Reason ?? "");
                    return JoinParagraphs(paragraphs);

                case "read_section":
                {
                    if (action.ParagraphStart is not { } rs || action.ParagraphEnd is not { } re)
                    {
                        lastToolResult = "Error: read_section requires paragraphStart and paragraphEnd (inclusive).";
                        break;
                    }

                    if (rs < 0 || re < rs || re >= paragraphs.Count)
                    {
                        lastToolResult =
                            $"Error: invalid range {rs}..{re} for draft with {paragraphs.Count} paragraphs (valid indices 0..{paragraphs.Count - 1}).";
                        break;
                    }

                    var slice = paragraphs.Skip(rs).Take(re - rs + 1).ToList();
                    var body = JoinParagraphs(slice);
                    lastToolResult = $"read_section result (paragraphs {rs}..{re}):\n{body}";
                    break;
                }

                case "propose_patch":
                {
                    if (action.ParagraphStart is not { } ps || action.ParagraphEnd is not { } pe)
                    {
                        lastToolResult = "Error: propose_patch requires paragraphStart and paragraphEnd (inclusive).";
                        break;
                    }

                    if (ps < 0 || pe < ps || pe >= paragraphs.Count)
                    {
                        lastToolResult =
                            $"Error: invalid range {ps}..{pe} for draft with {paragraphs.Count} paragraphs (valid indices 0..{paragraphs.Count - 1}).";
                        break;
                    }

                    var replacement = action.Replacement ?? "";
                    if (string.IsNullOrWhiteSpace(replacement))
                    {
                        lastToolResult = "Error: propose_patch requires non-empty replacement.";
                        break;
                    }

                    if (replacement.Length > MaxReplacementChars)
                    {
                        lastToolResult =
                            $"Error: replacement exceeds {MaxReplacementChars} characters.";
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
                        break;
                    }

                    lastToolResult =
                        $"propose_patch applied: replaced paragraphs {ps}..{pe}. Draft now has {paragraphs.Count} paragraphs.";
                    break;
                }

                default:
                    lastToolResult = $"Error: unknown action \"{action.Action}\". Use read_section, propose_patch, or finish.";
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
        string? lastToolResult)
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

        sb.AppendLine("Current draft (paragraph-index reference):");
        sb.AppendLine(numberedReference);
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
