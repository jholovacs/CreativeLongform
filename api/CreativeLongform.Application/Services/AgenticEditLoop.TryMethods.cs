using System.Diagnostics;
using CreativeLongform.Application.Generation;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Services;

public static partial class AgenticEditLoop
{
    internal static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    internal static Task<AgentToolExecuteResult> TryFinishAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        if (state.RunOptions?.RunComplianceAsync is not null)
        {
            if (state.LastComplianceVerdict is null)
            {
                var checksExhausted = state.ComplianceCheckCount >= state.RunOptions.MaxComplianceChecks;
                if (!(checksExhausted && state.DraftEditedSinceLastCompliance))
                {
                    var msg = checksExhausted
                        ? "Error: cannot finish — compliance check limit reached and draft was not edited since the last check. Apply fixInstructions, then finish (pipeline compliance will still run)."
                        : "Error: cannot finish — run_compliance_check on the current draft first. You must obtain pass:true before finish.";
                    return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg));
                }

                state.Logger.LogWarning("Agent finish with compliance checks exhausted; draft was edited but pass was not re-verified");
            }
            else if (!state.LastComplianceVerdict.Pass)
            {
                var msg = FormatComplianceMustFixBeforeFinish(state.LastComplianceVerdict);
                return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg));
            }
        }

        if (state.RunOptions?.RunQualityAsync is not null && state.RunOptions.RequireQualityBeforeFinish)
        {
            if (state.LastQualityVerdict is null)
            {
                var checksExhausted = state.QualityCheckCount >= state.RunOptions.MaxQualityChecks;
                if (!(checksExhausted && state.DraftEditedSinceLastQuality))
                {
                    var msg = checksExhausted
                        ? "Error: cannot finish — quality check limit reached and draft was not edited since the last check. Apply fixInstructions, then finish."
                        : "Error: cannot finish — run_quality_check on the current draft first.";
                    return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg));
                }

                state.Logger.LogWarning("Agent finish with quality checks exhausted; draft was edited but score was not re-verified");
            }
            else
            {
                var draftNow = JoinParagraphs(state.Paragraphs);
                if (HashDraft(draftNow) != state.LastQualityDraftHash)
                {
                    return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                        "Error: cannot finish — draft changed since last quality check. run_quality_check on the current draft first."));
                }

                var minScore = state.RunOptions.QualityReviewMinScore ?? 55;
                if (state.LastQualityVerdict.Score < minScore || state.LastQualityVerdict.FixInstructions.Count > 0)
                {
                    var msg = FormatQualityMustFixBeforeFinish(state.LastQualityVerdict, minScore);
                    return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg));
                }
            }
        }

        state.Logger.LogInformation("Agentic edit finished: {Reason}", action.Reason ?? "");
        var finishMsg = $"Editor finished (agent pass). Why: {Truncate(action.Reason ?? "(no reason)", 400)}";
        return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Finished, finishMsg));
    }

    internal static async Task<AgentToolExecuteResult> TryComplianceCheckAsync(
        AgentEditLoopState state,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        if (state.RunOptions?.RunComplianceAsync is null)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, "Error: run_compliance_check is not available.");

        if (state.ComplianceCheckCount >= state.RunOptions.MaxComplianceChecks)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: compliance check limit ({state.RunOptions.MaxComplianceChecks}) reached. Apply fixInstructions via edit tools.");

        var draftNow = JoinParagraphs(state.Paragraphs);
        var draftHash = HashDraft(draftNow);
        if (draftHash == state.LastComplianceDraftHash)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                "Error: draft unchanged since last compliance check. Apply fixes, then run_compliance_check again.");

        state.ComplianceCheckCount++;
        state.LastComplianceDraftHash = draftHash;
        var raw = await state.RunOptions.RunComplianceAsync(draftNow, state.CancellationToken);
        var grounded = ComplianceVerdictGrounding.GroundAgainstDraft(draftNow, raw);
        var verdict = LanguageContextShiftDetector.MergeIntoCompliance(grounded.Verdict,
            LanguageContextShiftDetector.Analyze(draftNow));
        state.LastComplianceVerdict = verdict;
        state.DraftEditedSinceLastCompliance = false;
        var msg = FormatComplianceToolResult(verdict, grounded.DroppedItems);
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, msg);
    }

    internal static async Task<AgentToolExecuteResult> TryQualityCheckAsync(
        AgentEditLoopState state,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        if (state.RunOptions?.RunQualityAsync is null)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, "Error: run_quality_check is not available.");

        if (state.QualityCheckCount >= state.RunOptions.MaxQualityChecks)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: quality check limit ({state.RunOptions.MaxQualityChecks}) reached. Apply fixInstructions via edit tools.");

        var draftNow = JoinParagraphs(state.Paragraphs);
        var draftHash = HashDraft(draftNow);
        if (draftHash == state.LastQualityDraftHash)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                "Error: draft unchanged since last quality check. Apply fixes, then run_quality_check again.");

        state.QualityCheckCount++;
        state.LastQualityDraftHash = draftHash;
        var verdict = await state.RunOptions.RunQualityAsync(draftNow, state.CancellationToken);
        state.LastQualityVerdict = verdict;
        state.DraftEditedSinceLastQuality = false;
        var msg = FormatQualityToolResult(verdict, state.RunOptions.QualityReviewMinScore ?? 55);
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, msg);
    }

    internal static async Task<AgentToolExecuteResult> TryProposePatchAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId,
        string source)
    {
        if (action.ParagraphStart is { } ps && action.ParagraphEnd is { } pe)
        {
            var originalSpan = JoinParagraphs(state.Paragraphs.Skip(ps).Take(pe - ps + 1).ToList());
            await AgentEditProgress.NotifyStatusAsync(state,
                AgentEditNarrative.DescribeApplyingReplace(originalSpan, action.Replacement ?? "", ps, pe), llmCallId);
        }

        var patchResult = await TryApplyParagraphReplacementAsync(
            state.Paragraphs, state.ReadRanges, state.AppliedEditKeys, action, state.Logger, source);
        if (patchResult.ToolResult.StartsWith("Error:", StringComparison.Ordinal))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, patchResult.ToolResult);
        state.MarkEdited();
        if (action.ParagraphStart is { } patchStart && action.ParagraphEnd is { } patchEnd)
            await WorkingDocumentNotifier.NotifyAgentStateAsync(state, $"propose_patch updated ¶{patchStart}..{patchEnd}");
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, AppendPostEditGuidance(state, patchResult.ToolResult));
    }

    internal static async Task<AgentToolExecuteResult> TryInvokeWriterAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId) =>
        await TryInvokeDelegatedAsync(state, action, turn, maxTurns, turnSw, llmCallId, "writer",
            state.RunOptions?.InvokeWriterAsync, "invoke_writer");

    internal static async Task<AgentToolExecuteResult> TryInvokeEditorAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId) =>
        await TryInvokeDelegatedAsync(state, action, turn, maxTurns, turnSw, llmCallId, "editor",
            state.RunOptions?.InvokeEditorAsync, "invoke_editor");

    internal static async Task<AgentToolExecuteResult> TryInvokeCorrectorAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId) =>
        await TryInvokeDelegatedAsync(state, action, turn, maxTurns, turnSw, llmCallId, "corrector",
            state.RunOptions?.InvokeCorrectorAsync, "invoke_corrector");

    private static async Task<AgentToolExecuteResult> TryInvokeDelegatedAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId,
        string kind,
        Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? invoke,
        string source)
    {
        if (invoke is null)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, $"Error: {source} is not available.");

        if (action.ParagraphStart is not { } ws || action.ParagraphEnd is not { } we)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, $"Error: {source} requires paragraphStart and paragraphEnd (inclusive).");

        var instruction = action.Instruction?.Trim() ?? "";
        if (string.IsNullOrEmpty(instruction))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, $"Error: {source} requires non-empty \"instruction\".");

        var key = EditKey(kind, ws, we, instruction);
        if (state.AppliedEditKeys.Contains(key))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: duplicate {source} on this range with the same instruction. Use a NEW instruction or another tool.");

        if (ShouldUseSummary(state.Paragraphs) && !IsRangeCoveredByReads(ws, we, state.ReadRanges))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: call read_section covering {ws}..{we} before {source}.");

        if (ws < 0 || we < ws || we >= state.Paragraphs.Count)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: invalid range {ws}..{we} for draft with {state.Paragraphs.Count} paragraphs.");

        var role = AgentEditNarrative.RoleDisplayName(kind);
        var originalSpan = JoinParagraphs(state.Paragraphs.Skip(ws).Take(we - ws + 1).ToList());

        string replacement;
        try
        {
            replacement = await invoke(
                BuildDelegationRequest(state.Paragraphs, ws, we, instruction, state.LastComplianceVerdict, action.ComplianceNotes,
                    action.FocusExcerpt, action.ContextParagraphsBefore, action.ContextParagraphsAfter),
                state.CancellationToken);
        }
        catch (Exception ex)
        {
            state.Logger.LogWarning(ex, "Agentic edit {Source} failed", source);
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, $"Error: {kind} model failed — {ex.Message}");
        }

        await AgentEditProgress.NotifyStatusAsync(state, AgentEditNarrative.DescribeDelegatedResponse(role, replacement), llmCallId);
        state.LastDelegatedRole = role;

        await AgentEditProgress.NotifyStatusAsync(state,
            AgentEditNarrative.DescribeApplyingReplace(originalSpan, replacement, ws, we), llmCallId);

        action.Replacement = LlmProseSanitizer.ProseForApplication(replacement);
        var patch = await TryApplyParagraphReplacementAsync(
            state.Paragraphs, state.ReadRanges, state.AppliedEditKeys, action, state.Logger, source,
            preapprovedEditKey: key);
        if (patch.ToolResult.StartsWith("Error:", StringComparison.Ordinal))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, patch.ToolResult);
        state.MarkEdited();
        await WorkingDocumentNotifier.NotifyAgentStateAsync(state, $"{role} updated ¶{ws}..{we}");
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, AppendPostEditGuidance(state, patch.ToolResult));
    }
}
