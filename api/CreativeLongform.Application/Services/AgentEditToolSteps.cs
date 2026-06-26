using System.Diagnostics;
using System.Text;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Services;

/// <summary>Executes individual agent tools (used by the main loop and run_script batches).</summary>
internal static class AgentEditToolSteps
{
    internal static async Task<AgentToolExecuteResult> ExecuteAsync(
        AgentEditLoopState state,
        AgentEditActionDto action,
        bool allowFinish,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        var kind = action.Action.Trim().ToLowerInvariant();
        var paragraphs = state.Paragraphs;

        if (!AgentToolRegistry.IsKnownAction(kind))
            return Err(AgentToolRegistry.UnknownToolMessage(kind));

        var misuse = AgentToolRegistry.ValidateToolUse(kind, action, paragraphs.Count);
        if (misuse is not null)
            return Err(misuse);

        switch (kind)
        {
            case "finish":
                if (!allowFinish)
                    return Err("Error: finish is not allowed inside run_script.");
                return await AgenticEditLoop.TryFinishAsync(state, action, turn, maxTurns, turnSw, llmCallId);

            case "run_script":
                return await RunScriptAsync(state, action, turn, maxTurns, turnSw, llmCallId);

            case "read_section":
                return ReadSection(state, action);

            case "find_text":
                return FindText(state, action);

            case "replace_text":
                return ReplaceText(state, action);

            case "swap_text":
                return SwapText(state, action);

            case "patch_text":
                return PatchText(state, action);

            case "query_lore":
                return QueryLore(state, action);

            case "query_timeline":
                return QueryTimeline(state, action);

            case "propose_patch":
                return await AgenticEditLoop.TryProposePatchAsync(state, action, turn, maxTurns, turnSw, llmCallId, "propose_patch");

            case "run_compliance_check":
                return await AgenticEditLoop.TryComplianceCheckAsync(state, turn, maxTurns, turnSw, llmCallId);

            case "invoke_writer":
                return await AgenticEditLoop.TryInvokeWriterAsync(state, action, turn, maxTurns, turnSw, llmCallId);

            case "invoke_editor":
                return await AgenticEditLoop.TryInvokeEditorAsync(state, action, turn, maxTurns, turnSw, llmCallId);

            case "invoke_corrector":
                return await AgenticEditLoop.TryInvokeCorrectorAsync(state, action, turn, maxTurns, turnSw, llmCallId);

            default:
                return Err(AgentToolRegistry.UnknownToolMessage(kind));
        }
    }

    internal static async Task<AgentToolExecuteResult> RunScriptAsync(
        AgentEditLoopState state,
        AgentEditActionDto scriptAction,
        int turn,
        int maxTurns,
        Stopwatch turnSw,
        Guid llmCallId)
    {
        var steps = scriptAction.Steps!;
        var report = new StringBuilder();
        report.AppendLine($"run_script ({steps.Count} step(s)):");

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var stepKind = step.Action?.Trim().ToLowerInvariant() ?? "";
            if (stepKind is "finish" or "run_script")
            {
                report.AppendLine($"  step {i + 1} FAILED: nested {stepKind} is not allowed in scripts.");
                return Err(report.ToString());
            }

            var stepLabel = $"Script step {i + 1}/{steps.Count}";
            await AgentEditProgress.NotifyActionAsync(state, turn, maxTurns, step, llmCallId, turnSw.ElapsedMilliseconds, stepLabel);
            var result = await ExecuteAsync(state, step, allowFinish: false, turn, maxTurns, turnSw, llmCallId);
            await AgentEditProgress.NotifyResultAsync(state, turn, maxTurns, stepKind, result.Status, result.Message,
                llmCallId, turnSw.ElapsedMilliseconds, step, stepLabel);
            report.AppendLine($"  step {i + 1} ({stepKind}): {AgenticEditLoop.Truncate(result.Message, 2000)}");
            if (result.Status == AgentToolExecuteStatus.Error)
            {
                report.AppendLine("  script halted on first error.");
                return Err(report.ToString().TrimEnd());
            }
        }

        report.AppendLine("  script completed successfully.");
        return Ok(report.ToString().TrimEnd());
    }

    private static AgentToolExecuteResult ReadSection(AgentEditLoopState state, AgentEditActionDto action)
    {
        var rs = action.ParagraphStart!.Value;
        var re = action.ParagraphEnd!.Value;
        var paragraphs = state.Paragraphs;
        if (rs < 0 || re < rs || re >= paragraphs.Count)
            return Err($"Error: invalid range {rs}..{re} for draft with {paragraphs.Count} paragraphs.");

        var body = AgenticEditLoop.JoinParagraphs(paragraphs.Skip(rs).Take(re - rs + 1).ToList());
        state.ReadRanges.Add((rs, re));
        return Ok($"read_section result (paragraphs {rs}..{re}):\n{body}");
    }

    private static AgentToolExecuteResult FindText(AgentEditLoopState state, AgentEditActionDto action)
    {
        var pattern = action.Pattern?.Trim() ?? action.Query?.Trim() ?? "";
        var find = AgentDraftTextTools.Find(
            state.Paragraphs, pattern, action.UseRegex == true, action.CaseSensitive == true,
            action.MaxMatches, action.ParagraphStart, action.ParagraphEnd);
        return find.Ok ? Ok(AgentDraftTextTools.FormatFindResult(find)) : Err(AgentDraftTextTools.FormatFindResult(find));
    }

    private static AgentToolExecuteResult ReplaceText(AgentEditLoopState state, AgentEditActionDto action)
    {
        var pattern = action.Pattern!.Trim();
        var replacement = action.Replacement ?? "";
        var replaceKey = AgenticEditLoop.EditKey("replace", action.ParagraphStart ?? 0, action.ParagraphEnd ?? state.Paragraphs.Count - 1,
            $"{pattern}\0{replacement}\0{action.PreviewOnly == true}");
        if (action.PreviewOnly != true && state.AppliedEditKeys.Contains(replaceKey))
            return Err("Error: identical replace_text was already applied. Change pattern/replacement or use previewOnly:true first.");

        var replace = AgentDraftTextTools.Replace(
            state.Paragraphs, pattern, replacement, action.UseRegex == true, action.CaseSensitive == true,
            action.MaxReplacements, action.ParagraphStart, action.ParagraphEnd, action.PreviewOnly == true);
        var msg = AgentDraftTextTools.FormatReplaceResult(replace);
        if (!replace.Ok)
            return Err(msg);
        if (replace.ReplacementsApplied > 0 && action.PreviewOnly != true)
        {
            state.AppliedEditKeys.Add(replaceKey);
            state.ReadRanges.Clear();
            state.MarkEdited();
            return Ok(AgenticEditLoop.AppendPostEditGuidance(msg));
        }

        return Ok(msg);
    }

    private static AgentToolExecuteResult SwapText(AgentEditLoopState state, AgentEditActionDto action)
    {
        var (selectionA, selectionB) = ResolveSwapSelections(action);
        var swapKey = AgenticEditLoop.EditKey("swap", action.ParagraphStart ?? 0, action.ParagraphEnd ?? state.Paragraphs.Count - 1,
            $"{selectionA}\0{selectionB}\0{action.PreviewOnly == true}");
        if (action.PreviewOnly != true && state.AppliedEditKeys.Contains(swapKey))
            return Err("Error: identical swap_text was already applied. Change selections or use previewOnly:true first.");

        var swap = AgentDraftTextTools.Swap(
            state.Paragraphs, selectionA, selectionB, action.UseRegex == true, action.CaseSensitive == true,
            action.ParagraphStart, action.ParagraphEnd, action.PreviewOnly == true);
        var msg = AgentDraftTextTools.FormatSwapResult(swap);
        if (!swap.Ok)
            return Err(msg);
        if (swap.ParagraphsModified.Count > 0 && action.PreviewOnly != true)
        {
            state.AppliedEditKeys.Add(swapKey);
            state.ReadRanges.Clear();
            state.MarkEdited();
            return Ok(AgenticEditLoop.AppendPostEditGuidance(msg));
        }

        return Ok(msg);
    }

    private static (string A, string B) ResolveSwapSelections(AgentEditActionDto action)
    {
        static string? Pick(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }

            return null;
        }

        return (Pick(action.ExcerptA, action.Excerpt, action.Pattern) ?? "",
            Pick(action.ExcerptB, action.Text, action.Replacement) ?? "");
    }

    private static AgentToolExecuteResult PatchText(AgentEditLoopState state, AgentEditActionDto action)
    {
        var mode = action.Mode!.Trim();
        var excerpt = action.Excerpt ?? action.Pattern ?? "";
        var text = action.Text ?? action.Replacement ?? "";
        var patch = AgentDraftTextTools.Patch(
            state.Paragraphs, mode, action.ParagraphStart!.Value, action.ParagraphEnd,
            excerpt, text, action.UseRegex == true, action.CaseSensitive == true);
        var msg = AgentDraftTextTools.FormatPatchResult(patch);
        if (!patch.Ok)
            return Err(msg);
        if (patch.ParagraphsModified.Count > 0)
        {
            state.ReadRanges.Clear();
            state.MarkEdited();
            return Ok(AgenticEditLoop.AppendPostEditGuidance(msg));
        }

        return Ok(msg);
    }

    private static AgentToolExecuteResult QueryLore(AgentEditLoopState state, AgentEditActionDto action)
    {
        if (state.RunOptions?.Lore is null)
            return Err("Error: query_lore is not available.");
        return Ok(state.RunOptions.Lore.Query(action.Query, action.Scope));
    }

    private static AgentToolExecuteResult QueryTimeline(AgentEditLoopState state, AgentEditActionDto action)
    {
        if (state.RunOptions?.Timeline is null)
            return Err("Error: query_timeline is not available.");
        return Ok(state.RunOptions.Timeline.Query(action.Query, action.When));
    }

    private static AgentToolExecuteResult Ok(string message) =>
        new(AgentToolExecuteStatus.Ok, message);

    private static AgentToolExecuteResult Err(string message) =>
        new(AgentToolExecuteStatus.Error, message);
}
