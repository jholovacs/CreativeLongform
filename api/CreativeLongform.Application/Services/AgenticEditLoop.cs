using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Agent;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Services;

/// <summary>
/// Paragraph-addressable tool loop: read_section, propose_patch, finish (JSON only).
/// </summary>
public static partial class AgenticEditLoop
{
    private const int FullDraftCharBudget = 60_000;
    private const int SummaryPrefixChars = 400;
    private const int MaxReplacementChars = 80_000;
    /// <summary>Max chars of raw model output embedded in tool feedback to the agent (not sent over SignalR).</summary>
    private const int ToolResultTruncationChars = 12_000;
    private const int MaxToolHistoryEntries = 16;
    private const int MaxToolHistoryChars = 10_000;
    private const int ToolHistoryResultTruncation = 2500;
    private const int ProgressPatchExcerptChars = 7000;

    private static readonly string SystemPrompt = """
        You are the orchestrating fiction editor agent — the center of the creative pipeline. You decide how to realize the author's vision: gather context, choose tools, delegate to specialist models when needed, independently verify their assertions, and finish only when checks pass.

        AGENT MANDATE:
        - Decide: every turn, state conclusion + nextStep before acting.
        - Delegate: invoke_writer (creative), invoke_editor (voice/format), invoke_corrector (mechanics) with focused instructions and context scope.
        - Verify: run_compliance_check and run_quality_check (when available) after substantive edits; never trust critic output without find_text grounding.
        - Resources: query_lore, query_timeline, read_section, find_text before editing; use run_script for batched surgical fixes.
        - Finish: only when mission/scene requirements are met AND verification passes on the CURRENT draft.

        BOOK DIRECTIVES (tone, content style, synopsis) appear in every user message — honor them in every edit and delegation.

        Respond with a single JSON object only (no markdown fences). Property names are case-insensitive.

        REFLECTION — required every turn (plain English for the author following the log):
        - "conclusion": What you infer from the current draft, recent tool history, and open compliance failures (1–2 sentences).
        - "nextStep": What this turn's chosen action will accomplish and why (1 sentence; must match "action").
        Example: { "conclusion": "Compliance still fails on past tense in ¶1.", "nextStep": "Invoke Editor on ¶1 to convert verbs to past tense.", "action": "invoke_editor", "paragraphStart": 1, "paragraphEnd": 1, "instruction": "…" }

        Tools:
        - { "action": "read_section", "paragraphStart": <int>, "paragraphEnd": <int> } — inclusive paragraph indices (0..N-1).
        - { "action": "find_text", "pattern": "<literal or regex>", "useRegex": <bool>, "caseSensitive": <bool>, "paragraphStart": <int optional>, "paragraphEnd": <int optional>, "maxMatches": <int optional> } — search the draft without an LLM; returns ¶index, char offset, and excerpt for each match.
        - { "action": "replace_text", "pattern": "<literal or regex>", "replacement": "<text>", "useRegex": <bool>, "caseSensitive": <bool>, "paragraphStart": <int optional>, "paragraphEnd": <int optional>, "maxReplacements": <int optional>, "previewOnly": <bool optional> } — programmatic find/replace (no LLM).
        - { "action": "swap_text", "excerptA": "<first selection>", "excerptB": "<second selection>", "useRegex": <bool>, "caseSensitive": <bool>, "paragraphStart": <int optional>, "paragraphEnd": <int optional>, "previewOnly": <bool optional> } — exchange two located text selections (same or different ¶s). Aliases: excerpt+text or pattern+replacement. Use find_text to locate unique excerpts first.
        - { "action": "patch_text", "mode": "<mode>", "paragraphStart": <int>, "paragraphEnd": <int optional>, "excerpt": "<locator>", "text": "<payload>", "useRegex": <bool>, "caseSensitive": <bool> } — surgical excerpt edits without full ¶ replace. Modes: replace_excerpt | remove_excerpt | insert_before_excerpt | insert_after_excerpt | append_paragraph | prepend_paragraph.
        - { "action": "query_lore", "query": "<keywords>", "scope": "scene"|"book"|"relationships"|"all" } — world elements, book notes, relationships.
        - { "action": "query_timeline", "query": "<keywords optional>", "when": "before"|"after"|"all"|"current" } — other scenes in story order for continuity (never contradict earlier/later canon).
        - { "action": "check_scene_brief" } — deterministic beat checklist vs scene instructions (run on turn 1 before editing).
        - { "action": "check_word_budget" } — current word count vs session target; recommends break_up_scene when short.
        - { "action": "break_up_scene", "beats": [ { "mode": "expand"|"insert_after", "paragraphStart", "paragraphEnd", "afterParagraph", "instruction", "targetWords" } ], "reason": "<why>" } — Writer expands thin ¶s or inserts new beats (high ¶ first); max 8 beats; requires full draft read.
        - { "action": "run_compliance_check" } — compliance on CURRENT full draft (perspective, POV, tense, grammar/punctuation, canon).
        - { "action": "run_quality_check" } — prose craft quality on CURRENT full draft (when available in this session).
        - { "action": "invoke_writer", "paragraphStart": <int>, "paragraphEnd": <int>, "instruction": "<brief>", "focusExcerpt": "<optional>", "contextParagraphsBefore": <int default 2>, "contextParagraphsAfter": <int default 2>, "complianceNotes": "<optional>", "reason": "<why>" } — creative rewrite; default 2 ¶ context each side unless you set 0.
        - { "action": "invoke_editor", ... same optional scope fields ... } — light touch-ups (tense, perspective, formatting).
        - { "action": "invoke_corrector", ... same optional scope fields ... } — grammar/punctuation.
        - { "action": "propose_patch", "paragraphStart": <int>, "paragraphEnd": <int>, "replacement": "<prose>", "reason": "<why>" } — micro-edits ONLY (≤45 words). Never paste substantive rewrites here.
        - { "action": "run_script", "steps": [ { ...tool json... }, ... ], "reason": "<why>" } — batch up to 12 surgical steps (find → patch → replace, multiple localized fixes). Stops on first error. No nested run_script or finish inside scripts.
        - { "action": "finish", "reason": "<short reason>" } — stop ONLY after required checks pass on the CURRENT draft (see finish rules below).

        Finish rules:
        - When run_compliance_check is available: pass:true on the CURRENT draft before finish.
        - When run_quality_check is available and required: score at or above the session threshold with no remaining fixInstructions on the CURRENT draft.
        - When an AUTHOR CORRECTION MISSION appears in the user message: finish only after you judge the mission fully implemented (state this in reason).

        Word-count strategy (when Word budget shows a deficit):
        - check_word_budget → map missing beats (check_scene_brief) → read_section full draft → break_up_scene with expand + insert_after beats (~300–450 words each).
        - Do NOT finish while substantially below MinWordsTarget unless the scene brief explicitly requires brevity.

        Script strategy (multi-target fixes):
        - Turn 1 MUST be planning only: read_section, find_text, query_lore, query_timeline, check_scene_brief, or run_*_check — inspect before editing.
        - Use run_script to chain find_text → patch_text/replace_text for several verified compliance items in one turn.
        - query_timeline / query_lore whenever continuity or canon is uncertain — always allowed alongside edits.
        - On tool misuse you receive corrective hints; on unknown actions you receive the full tool list. The loop only aborts after many consecutive failures — fix your JSON and retry.

        Text manipulation strategy:
        - find_text → replace_text, swap_text, or patch_text for mechanical/local fixes; invoke_* when rewriting phrasing or voice.
        - ALWAYS read_section covering ¶range before propose_patch, invoke_*, replace_text, swap_text, or patch_text on that range.
        - swap_text when two passages or phrases should trade places (e.g. reorder sentences, fix transposed phrases).
        - patch_text for insert/remove/replace around a unique excerpt without rewriting whole paragraphs.

        Compliance-driven correction loop (mandatory when run_compliance_check is available):
        1. run_compliance_check on the current draft.
        2. If pass:false — for EACH fixInstruction, find_text the quoted phrase on the CURRENT draft first. If not found, treat that item as a critic hallucination — skip it; do NOT edit the draft to match phantom text.
        3. read_section cited ranges, then fix only verified violations. Quote verified fixInstructions in invoke_* instructions.
        4. After substantive edits, run_compliance_check again until pass:true or turns exhausted.
        5. Re-invoke delegated models with NEW instructions when fixes were incomplete.
        6. Do not finish while pass:false with verified (draft-grounded) issues remaining.

        Model selection:
        - find_text + replace_text / swap_text / patch_text — mechanical fixes without an LLM.
        - propose_patch — micro-edits only (≤45 words). Substantive rewrites MUST use invoke_writer / invoke_editor / invoke_corrector.
        - invoke_corrector — grammar/spelling/punctuation.
        - invoke_editor — tense, POV, formatting.
        - invoke_writer — creative rewrite or voice overhaul.

        Strategy:
        - query_lore / query_timeline when unsure → read_section → fix tools or run_script → run_compliance_check → run_quality_check (when available) → finish.
        - Preserve plot-critical substance; never compress dramatized beats into summary.
        - Summarized draft view: read_section before invoke_* or propose_patch on that range.
        - After invoke_* you receive edit_diff and delegation_verification — re-read if warnings appear.
        - Indices refer to the CURRENT draft (after prior patches in this session).
        - Review "Recent tool history" before retrying find/replace or patch_text. If a pattern returned "no matches", use find_text on the CURRENT draft for exact text — do not repeat the same failed pattern.
        """;

    private static readonly string UserCorrectionSystemAddendum = """

        AUTHOR CORRECTION MODE — the user message includes an AUTHOR CORRECTION MISSION (primary goal for this session).
        - Turn 1: read the mission, inspect relevant draft ranges (read_section / find_text), and query lore or timeline when continuity is unclear. State your implementation plan in conclusion and nextStep before editing.
        - Gather as much context as needed via read_section, query_lore, and query_timeline before invoke_* or propose_patch.
        - Implement the mission surgically — preserve unrelated prose unless the mission requires broader changes.
        - After substantive edits: run_compliance_check, then run_quality_check (when available). Address verified fixInstructions; re-check until you judge the mission complete and checks pass.
        - finish only when the mission is done, compliance passes (when available), and quality is acceptable (when required).
        """;

    public static async Task<string> RunAsync(
        string initialDraft,
        string sceneInstructions,
        string? expectedEndNotes,
        string worldBlock,
        int maxTurns,
        ILogger logger,
        Func<string, string, CancellationToken, Task<(string messageText, string raw, Guid llmCallId)>> chatJsonAsync,
        IGenerationProgressNotifier notifier,
        Guid runId,
        Func<long> pipelineElapsedMs,
        CancellationToken cancellationToken,
        AgentEditRunOptions? runOptions = null)
    {
        var paragraphs = SplitParagraphs(initialDraft);
        if (paragraphs.Count == 0)
            return initialDraft;

        var state = new AgentEditLoopState
        {
            Paragraphs = paragraphs,
            RunOptions = runOptions,
            Logger = logger,
            Notifier = notifier,
            RunId = runId,
            PipelineElapsedMs = pipelineElapsedMs,
            CancellationToken = cancellationToken,
            WorkingDocumentRevision = runOptions?.InitialWorkingDocumentRevision ?? 0
        };

        if (state.WorkingDocumentRevision == 0)
        {
            await WorkingDocumentNotifier.NotifyAgentStateAsync(state, "Working document opened (initial draft)");
        }

        var maxConsecutiveFailures = runOptions?.MaxConsecutiveToolFailures ?? AgentToolRegistry.DefaultMaxConsecutiveFailures;
        var consecutiveFailures = 0;
        var systemPrompt = BuildSystemPrompt(runOptions);
        ComplianceVerdict? lastComplianceVerdict = null;
        QualityVerdict? lastQualityVerdict = null;
        string? lastToolResult = null;
        for (var turn = 1; turn <= maxTurns; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.TurnsUsed = turn;
            var numbered = BuildParagraphReference(paragraphs);
            var paragraphingWarning = BuildParagraphingWarning(paragraphs);
            var user = BuildUserMessage(
                sceneInstructions,
                expectedEndNotes,
                worldBlock,
                runOptions,
                JoinParagraphs(paragraphs),
                turn,
                maxTurns,
                paragraphs.Count,
                numbered,
                state.ToolHistory,
                paragraphingWarning,
                lastComplianceVerdict,
                lastQualityVerdict);

            await AgentEditProgress.NotifyStatusAsync(state, AgentEditNarrative.DescribeThinking(state));

            var turnSw = Stopwatch.StartNew();
            var (raw, _, llmCallId) = await chatJsonAsync(systemPrompt, user, cancellationToken);
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
                lastToolResult = $"Error: output was not valid JSON. Fix and try again. Raw (truncated): {Truncate(cleaned, ToolResultTruncationChars)}";
                consecutiveFailures++;
                lastToolResult = AgentToolRegistry.AppendFailureBudget(lastToolResult, consecutiveFailures, maxConsecutiveFailures);
                RecordToolHistory(state, turn, Truncate(cleaned, 1200), lastToolResult);
                await AgentEditProgress.NotifyActionAttemptAsync(state, turn, maxTurns, cleaned, llmCallId, turnSw.ElapsedMilliseconds,
                    "model returned invalid JSON — will retry");
                await AgentEditProgress.NotifyResultAsync(state, turn, maxTurns, "parse_error", AgentToolExecuteStatus.Error,
                    lastToolResult, llmCallId, turnSw.ElapsedMilliseconds);
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    logger.LogWarning("Agentic edit aborted after {Count} consecutive failures (invalid JSON)", consecutiveFailures);
                    break;
                }

                continue;
            }

            if (action is null || string.IsNullOrWhiteSpace(action.Action))
            {
                lastToolResult = "Error: missing \"action\" in JSON.";
                consecutiveFailures++;
                lastToolResult = AgentToolRegistry.AppendFailureBudget(lastToolResult, consecutiveFailures, maxConsecutiveFailures);
                RecordToolHistory(state, turn, Truncate(cleaned, 1200), lastToolResult);
                await AgentEditProgress.NotifyActionAttemptAsync(state, turn, maxTurns, cleaned, llmCallId, turnSw.ElapsedMilliseconds,
                    "JSON missing \"action\" field");
                await AgentEditProgress.NotifyResultAsync(state, turn, maxTurns, "parse_error", AgentToolExecuteStatus.Error,
                    lastToolResult, llmCallId, turnSw.ElapsedMilliseconds);
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    logger.LogWarning("Agentic edit aborted after {Count} consecutive failures (missing action)", consecutiveFailures);
                    break;
                }

                continue;
            }

            var kind = action.Action.Trim().ToLowerInvariant();
            var reflection = AgentEditNarrative.DescribeReflection(action);
            if (!string.IsNullOrEmpty(reflection))
            {
                state.LastConclusion = action.Conclusion?.Trim();
                state.LastNextStep = action.NextStep?.Trim();
                await AgentEditProgress.NotifyStatusAsync(state, reflection, llmCallId, turnSw.ElapsedMilliseconds);
            }

            await AgentEditProgress.NotifyActionAsync(state, turn, maxTurns, action, llmCallId, turnSw.ElapsedMilliseconds);
            var toolResult = await AgentEditToolSteps.ExecuteAsync(state, action, allowFinish: true, turn, maxTurns, turnSw, llmCallId);

            lastToolResult = toolResult.Message;
            lastComplianceVerdict = state.LastComplianceVerdict;
            lastQualityVerdict = state.LastQualityVerdict;
            RecordToolHistory(state, turn, AgentEditProgress.FormatActionJson(action), lastToolResult);

            if (toolResult.Status == AgentToolExecuteStatus.Error || AgentToolRegistry.IsErrorResult(lastToolResult))
            {
                consecutiveFailures++;
                lastToolResult = AgentToolRegistry.AppendFailureBudget(lastToolResult, consecutiveFailures, maxConsecutiveFailures);
                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    logger.LogWarning("Agentic edit aborted after {Count} consecutive tool failures", consecutiveFailures);
                }
            }
            else
            {
                consecutiveFailures = 0;
            }

            await AgentEditProgress.NotifyResultAsync(state, turn, maxTurns, kind, toolResult.Status, lastToolResult,
                llmCallId, turnSw.ElapsedMilliseconds, action);
            if (toolResult.Status == AgentToolExecuteStatus.Finished)
                return JoinParagraphs(paragraphs);

            state.LastToolName = kind;
            if (kind is not ("invoke_writer" or "invoke_editor" or "invoke_corrector"))
                state.LastDelegatedRole = null;

            if (toolResult.Status != AgentToolExecuteStatus.Finished)
                state.LastNarrativeHint = AgentEditNarrative.BuildContextForNextTurn(kind, lastToolResult, action, state);

            if (consecutiveFailures >= maxConsecutiveFailures)
                break;
        }

        if (consecutiveFailures >= maxConsecutiveFailures)
            logger.LogWarning("Agentic edit stopped after hitting consecutive failure limit ({Max})", maxConsecutiveFailures);
        else
            logger.LogWarning("Agentic edit stopped after {MaxTurns} turns without finish", maxTurns);

        AgentSessionMetrics.LogCompletion(
            state.RunId,
            state.RunOptions?.SessionKind,
            state.FinishedCleanly,
            state.TurnsUsed,
            maxTurns,
            state.ComplianceCheckCount,
            state.QualityCheckCount,
            state.DelegationCount,
            consecutiveFailures >= maxConsecutiveFailures,
            JoinParagraphs(paragraphs),
            logger);
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

    private static string BuildSystemPrompt(AgentEditRunOptions? runOptions)
    {
        if (string.IsNullOrWhiteSpace(runOptions?.UserCorrectionMission))
            return SystemPrompt;
        return SystemPrompt + UserCorrectionSystemAddendum;
    }

    private static string BuildUserMessage(
        string sceneInstructions,
        string? expectedEndNotes,
        string worldBlock,
        AgentEditRunOptions? runOptions,
        string fullDraft,
        int turn,
        int maxTurns,
        int paragraphCount,
        string numberedReference,
        IReadOnlyList<AgentToolHistoryEntry> toolHistory,
        string? paragraphingWarning,
        ComplianceVerdict? openCompliance,
        QualityVerdict? openQuality)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Turn {turn} of {maxTurns} (draft has {paragraphCount} paragraphs, indices 0..{Math.Max(0, paragraphCount - 1)}).");
        if (runOptions is { MinWordsTarget: > 0 })
        {
            var budget = AgentWordBudget.Analyze(fullDraft, runOptions.MinWordsTarget, runOptions.MaxWordsTarget, paragraphCount);
            sb.AppendLine($"Word budget: {budget.CurrentWords} / {budget.MinWords}–{budget.MaxWords} target" +
                          (budget.Deficit > 0 ? $" (short by {budget.Deficit} — consider check_word_budget and break_up_scene)." : "."));
            sb.AppendLine();
        }
        else
            sb.AppendLine();
        var missionBlock = FormatUserCorrectionMissionBlock(runOptions, fullDraft);
        if (!string.IsNullOrEmpty(missionBlock))
        {
            sb.AppendLine(missionBlock);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(runOptions?.BookDirectiveBlock))
        {
            sb.AppendLine(runOptions.BookDirectiveBlock);
            sb.AppendLine();
        }

        if (openCompliance is { Pass: false })
        {
            sb.AppendLine("OPEN COMPLIANCE FAILURES (fix these before finish; run_compliance_check again after edits):");
            foreach (var v in openCompliance.Violations)
                sb.AppendLine($"  • {v}");
            if (openCompliance.FixInstructions.Count > 0)
            {
                sb.AppendLine("Required fixes:");
                foreach (var f in openCompliance.FixInstructions)
                    sb.AppendLine($"  → {f}");
            }

            sb.AppendLine();
        }

        if (openQuality is not null && QualityNeedsAttention(openQuality, runOptions?.QualityReviewMinScore ?? 55))
        {
            sb.AppendLine("OPEN QUALITY ISSUES (address before finish; run_quality_check again after edits):");
            sb.AppendLine($"  score: {openQuality.Score:0}");
            foreach (var issue in openQuality.Issues)
                sb.AppendLine($"  • {issue}");
            if (openQuality.FixInstructions.Count > 0)
            {
                sb.AppendLine("Suggested craft fixes:");
                foreach (var f in openQuality.FixInstructions)
                    sb.AppendLine($"  → {f}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Scene instructions:");
        sb.AppendLine(sceneInstructions);
        sb.AppendLine();
        sb.AppendLine("Expected end notes (if any):");
        sb.AppendLine(string.IsNullOrEmpty(expectedEndNotes) ? "(none)" : expectedEndNotes);
        sb.AppendLine();
        sb.AppendLine("World context:");
        sb.AppendLine(worldBlock);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(runOptions?.ContinuityBriefBlock))
        {
            sb.AppendLine("Continuity anchor (state before this scene):");
            sb.AppendLine(runOptions.ContinuityBriefBlock);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(runOptions?.StateBeforeJson) && runOptions.StateBeforeJson.Trim() is var stateJson && stateJson != "{}" && stateJson.Length > 2)
        {
            sb.AppendLine("State before (JSON):");
            sb.AppendLine(stateJson);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(runOptions?.AuthorizedCastBlock))
        {
            sb.AppendLine(runOptions.AuthorizedCastBlock);
            sb.AppendLine();
        }

        var historyBlock = FormatToolHistory(toolHistory);
        if (!string.IsNullOrEmpty(historyBlock))
        {
            sb.AppendLine(historyBlock);
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

    internal static void EnsureReadCoversForEdit(IList<(int start, int end)> readRanges, int patchStart, int patchEnd)
    {
        foreach (var (rs, re) in readRanges)
        {
            if (rs <= patchStart && re >= patchEnd)
                return;
        }

        readRanges.Add((patchStart, patchEnd));
    }

    private static bool IsRangeCoveredByReads(int patchStart, int patchEnd, IReadOnlyList<(int start, int end)> reads)
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

    private sealed class ParagraphEditResult
    {
        public required string ToolResult { get; init; }
    }

    private static Task<ParagraphEditResult> TryApplyParagraphReplacementAsync(
        List<string> paragraphs,
        List<(int start, int end)> readRanges,
        HashSet<string> appliedEditKeys,
        AgentEditActionDto action,
        ILogger logger,
        string source,
        string? preapprovedEditKey = null)
    {
        if (action.ParagraphStart is not { } ps || action.ParagraphEnd is not { } pe)
        {
            var err = "Error: requires paragraphStart and paragraphEnd (inclusive).";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        if (ps < 0 || pe < ps || pe >= paragraphs.Count)
        {
            var err =
                $"Error: invalid range {ps}..{pe} for draft with {paragraphs.Count} paragraphs (valid indices 0..{paragraphs.Count - 1}).";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        if (source != "break_up_scene")
        {
            if (ShouldUseSummary(paragraphs) && !IsRangeCoveredByReads(ps, pe, readRanges))
            {
                var err =
                    $"Error: summarized draft view only shows previews. On a prior turn, call read_section covering {ps}..{pe} before editing.";
                return Task.FromResult(new ParagraphEditResult { ToolResult = err });
            }

            if (!IsRangeCoveredByReads(ps, pe, readRanges))
            {
                var err = RequireReadRangeError(readRanges, ps, pe, source)!;
                return Task.FromResult(new ParagraphEditResult { ToolResult = err });
            }
        }

        var replacement = LlmProseSanitizer.ProseForApplication(action.Replacement ?? "");
        if (string.IsNullOrWhiteSpace(replacement))
        {
            var err = "Error: requires non-empty replacement prose.";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        if (replacement.Length > MaxReplacementChars)
        {
            var err = $"Error: replacement exceeds {MaxReplacementChars} characters.";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        var patchKey = preapprovedEditKey ?? EditKey("patch", ps, pe, replacement);
        if (appliedEditKeys.Contains(patchKey))
        {
            var err =
                "Error: identical edit on this range was already applied. Change the text or instruction, run_compliance_check, or re-invoke with a revised instruction.";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        var originalSpan = JoinParagraphs(paragraphs.Skip(ps).Take(pe - ps + 1).ToList());
        var originalWords = CountWords(originalSpan);
        var replacementWords = CountWords(replacement);
        if (originalWords >= 50 && replacementWords < originalWords * 0.55)
        {
            logger.LogWarning(
                "Agentic edit: rejected {Source} for excessive shortening ({OriginalWords} -> {ReplacementWords} words)",
                source, originalWords, replacementWords);
            var err =
                $"Error: replacement is much shorter than the replaced span (~{originalWords} words vs ~{replacementWords}). Preserve plot and on-page events.";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        try
        {
            ApplyPatch(paragraphs, ps, pe, replacement);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Agentic edit {Source} patch failed", source);
            var err = $"Error applying patch: {ex.Message}";
            return Task.FromResult(new ParagraphEditResult { ToolResult = err });
        }

        appliedEditKeys.Add(patchKey);
        if (source != "break_up_scene")
            readRanges.Clear();
        var ok =
            $"{source} applied: replaced paragraphs {ps}..{pe}. Draft now has {paragraphs.Count} paragraphs.";
        var patchDetail = BuildProposePatchProgressDetail(ps, pe, action.Reason, originalSpan, replacement);
        var diff = AgentEditDiff.Format(originalSpan, replacement);
        return Task.FromResult(new ParagraphEditResult { ToolResult = $"{ok}\n\n{patchDetail}\n\n{diff}" });
    }

    private static AgentWriterInvokeRequest BuildDelegationRequest(
        AgentEditLoopState state,
        int paragraphStart,
        int paragraphEnd,
        string instruction,
        string? complianceNotes,
        string? focusExcerpt,
        int? contextParagraphsBefore,
        int? contextParagraphsAfter,
        int? targetWords = null)
    {
        var paragraphs = state.Paragraphs;
        var spanText = JoinParagraphs(paragraphs.Skip(paragraphStart).Take(paragraphEnd - paragraphStart + 1).ToList());
        var before = Math.Clamp(contextParagraphsBefore ?? 2, 0, paragraphStart);
        var after = Math.Clamp(contextParagraphsAfter ?? 2, 0, Math.Max(0, paragraphs.Count - 1 - paragraphEnd));
        var contextBefore = before > 0
            ? JoinParagraphs(paragraphs.Skip(paragraphStart - before).Take(before).ToList())
            : "";
        var contextAfter = after > 0
            ? JoinParagraphs(paragraphs.Skip(paragraphEnd + 1).Take(after).ToList())
            : "";
        return new AgentWriterInvokeRequest
        {
            ParagraphStart = paragraphStart,
            ParagraphEnd = paragraphEnd,
            Instruction = instruction,
            SpanText = spanText,
            FullDraft = JoinParagraphs(paragraphs),
            ComplianceContext = BuildDelegationComplianceContext(state.LastComplianceVerdict, complianceNotes),
            QualityContext = BuildDelegationQualityContext(state.LastQualityVerdict, paragraphStart, paragraphEnd),
            FocusExcerpt = focusExcerpt?.Trim() ?? "",
            ContextParagraphsBefore = before,
            ContextParagraphsAfter = after,
            ContextBeforeText = contextBefore,
            ContextAfterText = contextAfter,
            TargetWords = targetWords
        };
    }

    private static string FormatComplianceToolResult(ComplianceVerdict verdict, IReadOnlyList<string>? droppedItems = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("compliance_check result:");
        sb.AppendLine($"  pass: {verdict.Pass.ToString().ToLowerInvariant()}");
        sb.AppendLine("  violations:");
        if (verdict.Violations.Count == 0)
            sb.AppendLine("    (none)");
        else
            foreach (var v in verdict.Violations)
                sb.AppendLine($"    - {v}");
        sb.AppendLine("  fixInstructions:");
        if (verdict.FixInstructions.Count == 0)
            sb.AppendLine("    (none)");
        else
            foreach (var f in verdict.FixInstructions)
                sb.AppendLine($"    - {f}");
        if (droppedItems is { Count: > 0 })
        {
            sb.AppendLine("  dropped (critic cited text not in draft or echoed prompt rules — ignore):");
            foreach (var d in droppedItems)
                sb.AppendLine($"    - {d}");
        }

        if (!verdict.Pass)
        {
            sb.AppendLine();
            sb.AppendLine("  next_steps:");
            sb.AppendLine("    1. find_text each quoted phrase in fixInstructions — skip items with no matches (hallucination).");
            sb.AppendLine("    2. read_section each cited paragraph range for verified items only.");
            sb.AppendLine("    3. invoke_editor / invoke_writer / invoke_corrector / propose_patch / replace_text — verified fixInstructions only.");
            sb.AppendLine("    4. run_compliance_check again. Do NOT finish until pass:true.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatComplianceMustFixBeforeFinish(ComplianceVerdict verdict)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Error: cannot finish — last compliance check failed. Address every violation, then run_compliance_check until pass:true.");
        sb.AppendLine("Outstanding violations:");
        foreach (var v in verdict.Violations)
            sb.AppendLine($"  • {v}");
        if (verdict.FixInstructions.Count > 0)
        {
            sb.AppendLine("Apply these fixInstructions (quote them in your next invoke_* instruction):");
            foreach (var f in verdict.FixInstructions)
                sb.AppendLine($"  → {f}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string BuildDelegationComplianceContext(ComplianceVerdict? lastCompliance, string? complianceNotes)
    {
        if (!string.IsNullOrWhiteSpace(complianceNotes))
            return complianceNotes.Trim();
        if (lastCompliance is null || lastCompliance.Pass)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine("From last compliance check — address every item that applies to this passage:");
        foreach (var v in lastCompliance.Violations)
            sb.AppendLine($"- Violation: {v}");
        foreach (var f in lastCompliance.FixInstructions)
            sb.AppendLine($"- Fix: {f}");
        return sb.ToString().TrimEnd();
    }

    private static string BuildDelegationQualityContext(QualityVerdict? lastQuality, int paragraphStart, int paragraphEnd)
    {
        if (lastQuality is null || lastQuality.FixInstructions.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("From last quality check — craft fixes that may apply to this passage:");
        var matched = false;
        foreach (var fix in lastQuality.FixInstructions)
        {
            if (fix.Contains($"¶{paragraphStart}", StringComparison.Ordinal)
                || fix.Contains($"paragraph {paragraphStart}", StringComparison.OrdinalIgnoreCase)
                || fix.Contains($"¶{paragraphEnd}", StringComparison.Ordinal))
            {
                sb.AppendLine($"- Fix: {fix}");
                matched = true;
            }
        }

        if (!matched)
        {
            foreach (var fix in lastQuality.FixInstructions.Take(4))
                sb.AppendLine($"- Fix: {fix}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatUserCorrectionMissionBlock(AgentEditRunOptions? runOptions, string fullDraft)
    {
        if (string.IsNullOrWhiteSpace(runOptions?.UserCorrectionMission))
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("AUTHOR CORRECTION MISSION (primary goal — implement fully before finish):");
        sb.AppendLine(runOptions.UserCorrectionMission.Trim());
        if (runOptions.SelectionStart is int start && runOptions.SelectionEnd is int end && end > start &&
            start >= 0 && end <= fullDraft.Length)
        {
            sb.AppendLine();
            sb.AppendLine(
                $"SELECTION FOCUS — UTF-16 indices {start}..{end} exclusive (prioritize edits here; keep surrounding prose unless the mission requires more):");
            sb.AppendLine("---");
            sb.AppendLine(fullDraft[start..end]);
            sb.AppendLine("---");
        }

        return sb.ToString().TrimEnd();
    }

    private static bool QualityNeedsAttention(QualityVerdict verdict, double reviewMin) =>
        verdict.Score < reviewMin || verdict.FixInstructions.Count > 0;

    private static string FormatQualityToolResult(QualityVerdict verdict, double reviewMin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("quality_check result:");
        sb.AppendLine($"  score: {verdict.Score:0}");
        sb.AppendLine("  issues:");
        if (verdict.Issues.Count == 0)
            sb.AppendLine("    (none)");
        else
            foreach (var issue in verdict.Issues)
                sb.AppendLine($"    - {issue}");
        sb.AppendLine("  fixInstructions:");
        if (verdict.FixInstructions.Count == 0)
            sb.AppendLine("    (none)");
        else
            foreach (var f in verdict.FixInstructions)
                sb.AppendLine($"    - {f}");

        if (QualityNeedsAttention(verdict, reviewMin))
        {
            sb.AppendLine();
            sb.AppendLine("  next_steps:");
            sb.AppendLine("    1. read_section cited ranges for craft issues tied to your edits.");
            sb.AppendLine("    2. invoke_writer / invoke_editor / invoke_corrector / patch tools — address fixInstructions.");
            sb.AppendLine("    3. run_quality_check again. Do NOT finish while score is below threshold or fixInstructions remain.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatQualityMustFixBeforeFinish(QualityVerdict verdict, double reviewMin)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Error: cannot finish — last quality check score {verdict.Score:0} (need >={reviewMin:0}) or fixInstructions remain. Address craft issues, then run_quality_check until clear.");
        if (verdict.Issues.Count > 0)
        {
            sb.AppendLine("Outstanding issues:");
            foreach (var issue in verdict.Issues)
                sb.AppendLine($"  • {issue}");
        }

        if (verdict.FixInstructions.Count > 0)
        {
            sb.AppendLine("Apply these fixInstructions:");
            foreach (var f in verdict.FixInstructions)
                sb.AppendLine($"  → {f}");
        }

        return sb.ToString().TrimEnd();
    }

    internal static string AppendPostEditGuidance(AgentEditLoopState state, string toolResult)
    {
        var checks = state.RunOptions?.RunQualityAsync is not null && state.RunOptions.RunComplianceAsync is not null
            ? "run_compliance_check and run_quality_check"
            : state.RunOptions?.RunComplianceAsync is not null
                ? "run_compliance_check"
                : state.RunOptions?.RunQualityAsync is not null
                    ? "run_quality_check"
                    : "run_compliance_check";
        return toolResult
               + $"\n\nDraft edited — check status is stale. {checks}. If failures remain, re-invoke with a NEW instruction quoting remaining fixInstructions.";
    }

    internal static string EditKey(string kind, int start, int end, string payload) =>
        $"{kind}:{start}:{end}:{payload.Trim()}";

    private static string HashDraft(string draft)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(draft));
        return Convert.ToHexString(bytes);
    }

    internal static void RecordToolHistory(AgentEditLoopState state, int turn, string requestSummary, string result)
    {
        state.ToolHistory.Add(new AgentToolHistoryEntry(
            turn,
            requestSummary.Trim(),
            Truncate(result, ToolHistoryResultTruncation),
            AgentToolRegistry.IsErrorResult(result)));
        TrimToolHistory(state);
    }

    private static void TrimToolHistory(AgentEditLoopState state)
    {
        while (state.ToolHistory.Count > MaxToolHistoryEntries)
            state.ToolHistory.RemoveAt(0);

        while (state.ToolHistory.Count > 0 && ToolHistoryCharCount(state.ToolHistory) > MaxToolHistoryChars)
            state.ToolHistory.RemoveAt(0);
    }

    private static int ToolHistoryCharCount(IReadOnlyList<AgentToolHistoryEntry> history)
    {
        var n = 0;
        foreach (var entry in history)
            n += entry.RequestSummary.Length + entry.Result.Length + 32;
        return n;
    }

    private static string FormatToolHistory(IReadOnlyList<AgentToolHistoryEntry> history)
    {
        if (history.Count == 0)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine(
            "Recent tool history (oldest first — review before retrying find/replace; do not repeat patterns that returned no matches):");
        foreach (var entry in history)
        {
            sb.AppendLine($"Turn {entry.Turn} request:");
            sb.AppendLine(entry.RequestSummary);
            sb.Append("Turn ").Append(entry.Turn).Append(" result");
            if (entry.IsError)
                sb.Append(" (error)");
            sb.AppendLine(":");
            sb.AppendLine(entry.Result);
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

}
