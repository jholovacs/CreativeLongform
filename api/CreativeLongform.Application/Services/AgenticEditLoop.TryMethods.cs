using System.Diagnostics;
using System.Text;
using CreativeLongform.Application.Agent;
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

    internal static AgentDeterministicGuards.GuardContext BuildGuardContext(AgentEditRunOptions? options) =>
        new(options?.NarrativePerspective, options?.NarrativeTense, options?.ExpectedEndNotes, options?.StateBeforeJson);

    internal static string? RequireReadRangeError(IReadOnlyList<(int start, int end)> readRanges, int ps, int pe, string toolName)
    {
        if (IsRangeCoveredByReads(ps, pe, readRanges))
            return null;
        return $"Error: call read_section covering {ps}..{pe} before {toolName}.";
    }

    internal static async Task<AgentToolExecuteResult> TryFinishAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        var draftNow = JoinParagraphs(state.Paragraphs);
        var draftHash = HashDraft(draftNow);
        var guard = BuildGuardContext(state.RunOptions);

        if (state.RunOptions?.RunComplianceAsync is not null)
        {
            var complianceStale = state.LastComplianceVerdict is null
                                  || !state.LastComplianceVerdict.Pass
                                  || state.LastComplianceDraftHash != draftHash;
            if (complianceStale)
            {
                if (state.ComplianceCheckCount >= state.RunOptions.MaxComplianceChecks)
                {
                    return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                        "Error: cannot finish — compliance check limit reached. Apply fixInstructions, then run_compliance_check again.");
                }

                await AgentEditProgress.NotifyStatusAsync(state,
                    "Agent running final compliance verification on the current draft before finish.", llmCallId);
                state.ComplianceCheckCount++;
                state.LastComplianceDraftHash = draftHash;
                var raw = await state.RunOptions.RunComplianceAsync(draftNow, state.CancellationToken);
                var processed = AgentVerification.ProcessCompliance(draftNow, raw, guard);
                state.LastComplianceVerdict = processed.Verdict;
                state.DraftEditedSinceLastCompliance = false;
                if (!processed.Verdict.Pass)
                {
                    var msg = FormatComplianceMustFixBeforeFinish(processed.Verdict);
                    return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg);
                }
            }
        }

        if (state.RunOptions?.RunQualityAsync is not null && state.RunOptions.RequireQualityBeforeFinish)
        {
            var qualityStale = state.LastQualityVerdict is null
                               || state.LastQualityDraftHash != draftHash
                               || QualityNeedsAttention(state.LastQualityVerdict,
                                   state.RunOptions.QualityReviewMinScore ?? 55);
            if (qualityStale)
            {
                if (state.QualityCheckCount >= state.RunOptions.MaxQualityChecks)
                {
                    return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                        "Error: cannot finish — quality check limit reached. Apply fixInstructions, then run_quality_check again.");
                }

                await AgentEditProgress.NotifyStatusAsync(state,
                    "Agent running final quality verification on the current draft before finish.", llmCallId);
                state.QualityCheckCount++;
                state.LastQualityDraftHash = draftHash;
                var rawQuality = await state.RunOptions.RunQualityAsync(draftNow, state.CancellationToken);
                var verdict = AgentVerification.ProcessQuality(draftNow, rawQuality, guard);
                state.LastQualityVerdict = verdict;
                state.DraftEditedSinceLastQuality = false;
                var minScore = state.RunOptions.QualityReviewMinScore ?? 55;
                if (QualityNeedsAttention(verdict, minScore))
                {
                    var msg = FormatQualityMustFixBeforeFinish(verdict, minScore);
                    return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, msg);
                }
            }
        }

        if (state.LastQualityVerdict?.Score is { } finishScore
            && state.RunOptions?.QualityAcceptMinScore is { } acceptMin
            && finishScore < acceptMin)
        {
            await AgentEditProgress.NotifyStatusAsync(state,
                $"Agent finish: score {finishScore:0} meets the review bar but is below the polish target ({acceptMin:0}) — author may want another pass.",
                llmCallId);
        }

        state.FinishedCleanly = true;
        state.Logger.LogInformation("Agentic edit finished: {Reason}", action.Reason ?? "");
        var finishMsg = $"Editor finished (agent pass). Why: {Truncate(action.Reason ?? "(no reason)", 400)}";
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Finished, finishMsg);
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
        var processed = AgentVerification.ProcessCompliance(draftNow, raw, BuildGuardContext(state.RunOptions));
        state.LastComplianceVerdict = processed.Verdict;
        state.DraftEditedSinceLastCompliance = false;
        state.PlanningTurnComplete = true;
        var msg = FormatComplianceToolResult(processed.Verdict, processed.DroppedItems);
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
        var raw = await state.RunOptions.RunQualityAsync(draftNow, state.CancellationToken);
        var verdict = AgentVerification.ProcessQuality(draftNow, raw, BuildGuardContext(state.RunOptions));
        state.LastQualityVerdict = verdict;
        state.DraftEditedSinceLastQuality = false;
        state.PlanningTurnComplete = true;
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
            EnsureReadCoversForEdit(state.ReadRanges, ps, pe);

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

        EnsureReadCoversForEdit(state.ReadRanges, ws, we);

        var key = EditKey(kind, ws, we, instruction);
        if (state.AppliedEditKeys.Contains(key))
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: duplicate {source} on this range with the same instruction. Use a NEW instruction or another tool.");

        if (ws < 0 || we < ws || we >= state.Paragraphs.Count)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: invalid range {ws}..{we} for draft with {state.Paragraphs.Count} paragraphs.");

        var role = AgentEditNarrative.RoleDisplayName(kind);
        var originalSpan = JoinParagraphs(state.Paragraphs.Skip(ws).Take(we - ws + 1).ToList());

        await AgentEditProgress.NotifyStatusAsync(state,
            $"Calling {role} model for ¶{ws}..{we}…", llmCallId);

        string replacement;
        try
        {
            replacement = await invoke(
                BuildDelegationRequest(state, ws, we, instruction, action.ComplianceNotes,
                    action.FocusExcerpt, action.ContextParagraphsBefore, action.ContextParagraphsAfter),
                state.CancellationToken);
        }
        catch (Exception ex)
        {
            state.Logger.LogWarning(ex, "Agentic edit {Source} failed", source);
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, $"Error: {kind} model failed — {ex.Message}");
        }

        state.DelegationCount++;
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

        var verification = AgentDelegationVerifier.Assess(instruction, originalSpan, replacement);
        var diff = AgentEditDiff.Format(originalSpan, replacement);
        var combined = $"{patch.ToolResult}\n\n{diff}\n\n{verification}";
        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, AppendPostEditGuidance(state, combined));
    }

    internal static Task<AgentToolExecuteResult> TryCheckWordBudgetAsync(AgentEditLoopState state)
    {
        var opts = state.RunOptions;
        var draft = JoinParagraphs(state.Paragraphs);
        var min = opts?.MinWordsTarget ?? 1;
        var max = opts?.MaxWordsTarget ?? min;
        var analysis = AgentWordBudget.Analyze(draft, min, max, state.Paragraphs.Count);
        state.PlanningTurnComplete = true;
        return Task.FromResult(new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, AgentWordBudget.FormatCheckResult(analysis)));
    }

    internal static async Task<AgentToolExecuteResult> TryBreakUpSceneAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        Guid llmCallId)
    {
        if (state.RunOptions?.InvokeWriterAsync is null)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, "Error: break_up_scene requires Writer delegation (not available).");

        var maxBreakUps = state.RunOptions.MaxSceneBreakUps;
        if (state.SceneBreakUpCount >= maxBreakUps)
            return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                $"Error: break_up_scene limit ({maxBreakUps}) reached for this session.");

        var last = state.Paragraphs.Count - 1;
        if (last >= 0)
        {
            var readErr = RequireReadRangeError(state.ReadRanges, 0, last, "break_up_scene");
            if (readErr is not null)
                return new AgentToolExecuteResult(AgentToolExecuteStatus.Error,
                    readErr + " Read the full draft before breaking up the scene.");
        }

        var beats = action.Beats!;
        var ordered = beats
            .Select((b, i) => (Beat: b, Index: i, SortKey: BeatSortKey(b)))
            .OrderByDescending(x => x.SortKey)
            .ThenByDescending(x => x.Index)
            .ToList();

        state.SceneBreakUpCount++;
        var report = new StringBuilder();
        report.AppendLine($"break_up_scene ({beats.Count} beat(s)):");
        if (!string.IsNullOrWhiteSpace(action.Reason))
            report.AppendLine($"  reason: {action.Reason.Trim()}");

        var wordsBefore = AgentWordBudget.CountWords(JoinParagraphs(state.Paragraphs));

        for (var n = 0; n < ordered.Count; n++)
        {
            var (beat, origIdx, _) = ordered[n];
            var mode = (beat.Mode ?? "expand").Trim().ToLowerInvariant();
            await AgentEditProgress.NotifyStatusAsync(state,
                $"Agent breaking up scene — Writer beat {n + 1}/{ordered.Count} ({mode})", llmCallId);

            int ws, we;
            if (mode is "insert_after")
            {
                var after = beat.AfterParagraph!.Value;
                var insertAt = after + 1;
                state.Paragraphs.Insert(insertAt, "…");
                ws = we = insertAt;
            }
            else
            {
                ws = beat.ParagraphStart!.Value;
                we = beat.ParagraphEnd!.Value;
            }

            var instruction = AgentWordBudget.BuildWriterBeatInstruction(beat.Instruction, beat.TargetWords);
            var originalSpan = JoinParagraphs(state.Paragraphs.Skip(ws).Take(we - ws + 1).ToList());

            string replacement;
            try
            {
                replacement = await state.RunOptions.InvokeWriterAsync(
                    BuildDelegationRequest(state, ws, we, instruction, null, null, null, null, beat.TargetWords),
                    state.CancellationToken);
            }
            catch (Exception ex)
            {
                state.Logger.LogWarning(ex, "break_up_scene beat {Index} failed", origIdx);
                report.AppendLine($"  beat {origIdx + 1} FAILED: {ex.Message}");
                report.AppendLine("  halted on first writer error.");
                return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, report.ToString().TrimEnd());
            }

            state.DelegationCount++;
            replacement = LlmProseSanitizer.ProseForApplication(replacement);
            var patchAction = new AgentEditActionDto
            {
                ParagraphStart = ws,
                ParagraphEnd = we,
                Replacement = replacement
            };
            var patch = await TryApplyParagraphReplacementAsync(
                state.Paragraphs, state.ReadRanges, state.AppliedEditKeys, patchAction, state.Logger, "break_up_scene",
                preapprovedEditKey: EditKey("break_up", ws, we, instruction));
            if (patch.ToolResult.StartsWith("Error:", StringComparison.Ordinal))
            {
                report.AppendLine($"  beat {origIdx + 1} ({mode}) FAILED: {patch.ToolResult}");
                report.AppendLine("  halted on first patch error.");
                return new AgentToolExecuteResult(AgentToolExecuteStatus.Error, report.ToString().TrimEnd());
            }

            var beatWords = AgentWordBudget.CountWords(replacement);
            report.AppendLine($"  beat {origIdx + 1} ({mode}) ¶{ws}..{we}: +{beatWords} words — {Truncate(beat.Instruction, 120)}");
            report.AppendLine(AgentEditDiff.Format(originalSpan, replacement));
        }

        state.MarkEdited();
        state.ReadRanges.Clear();
        await WorkingDocumentNotifier.NotifyAgentStateAsync(state, $"break_up_scene applied {beats.Count} beat(s)");

        var wordsAfter = AgentWordBudget.CountWords(JoinParagraphs(state.Paragraphs));
        report.AppendLine($"  draftWords: {wordsBefore} → {wordsAfter} (target min {state.RunOptions.MinWordsTarget}).");
        report.AppendLine("  next: check_word_budget, then run_compliance_check / run_quality_check.");

        return new AgentToolExecuteResult(AgentToolExecuteStatus.Ok, AppendPostEditGuidance(state, report.ToString().TrimEnd()));
    }

    private static int BeatSortKey(AgentSceneBeatDto beat)
    {
        var mode = (beat.Mode ?? "expand").Trim().ToLowerInvariant();
        if (mode is "insert_after")
            return beat.AfterParagraph ?? 0;
        return beat.ParagraphEnd ?? beat.ParagraphStart ?? 0;
    }
}
