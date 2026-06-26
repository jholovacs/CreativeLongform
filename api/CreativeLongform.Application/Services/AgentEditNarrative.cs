using System.Text;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Services;

/// <summary>Human-readable agent activity lines for the generation event log.</summary>
internal static class AgentEditNarrative
{
    private const int QuoteMaxChars = 72;
    private const int PurposeMaxChars = 100;

    public static string QuoteForLog(string? text, int maxChars = QuoteMaxChars)
    {
        var flat = CollapseWhitespace(text ?? "");
        if (flat.Length == 0)
            return "";
        if (flat.Length > maxChars)
            flat = flat[..maxChars] + " (truncated)";
        return $"'{flat}'";
    }

    public static string OptionalQuote(string? text, string fallbackDescription, int maxChars = QuoteMaxChars)
    {
        var quoted = QuoteForLog(text, maxChars);
        return quoted.Length > 0 ? quoted : fallbackDescription;
    }

    public static string DescribeThinking(AgentEditLoopState state)
    {
        var subject = BuildThinkingSubject(state);
        if (!string.IsNullOrWhiteSpace(state.LastConclusion))
        {
            var recap = TruncatePlain(CollapseWhitespace(state.LastConclusion), 160);
            return $"Agent is thinking about {subject} — last conclusion: {recap}";
        }

        return $"Agent is thinking about {subject}.";
    }

    /// <summary>Agent's stated conclusion and planned next step for the event log.</summary>
    public static string DescribeReflection(AgentEditActionDto action)
    {
        var conclusion = CollapseWhitespace(FirstNonEmpty(action.Conclusion, action.Reason) ?? "");
        var nextStep = CollapseWhitespace(action.NextStep ?? "");
        if (string.IsNullOrEmpty(conclusion) && string.IsNullOrEmpty(nextStep))
            return "";

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(conclusion))
            sb.Append("Agent concluded: ").AppendLine(TruncatePlain(conclusion, 320));
        if (!string.IsNullOrEmpty(nextStep))
            sb.Append("Next step: ").Append(TruncatePlain(nextStep, 240));
        else if (!string.IsNullOrWhiteSpace(action.Action))
            sb.Append("Next step: ").Append(TruncatePlain(DescribeActionSummary(action), 240));

        return sb.ToString().TrimEnd();
    }

    private static string BuildThinkingSubject(AgentEditLoopState state)
    {
        if (!string.IsNullOrWhiteSpace(state.LastNarrativeHint))
            return state.LastNarrativeHint.Trim();

        if (state.LastToolName is null && state.ComplianceCheckCount == 0)
            return "the draft and how to begin editing";

        return state.LastToolName switch
        {
            "invoke_writer" => "the Writer's rewrite and what to do next",
            "invoke_editor" => "the Editor's rewrite and what to do next",
            "invoke_corrector" => "the Corrector's fixes and what to do next",
            "run_compliance_check" => "the compliance results and what still needs fixing",
            "read_section" => "the passage it just read",
            "find_text" => "the search matches",
            "replace_text" or "patch_text" or "swap_text" or "propose_patch" => "the edit it just applied",
            "run_script" => "the script results",
            "query_lore" => "the lore it found",
            "query_timeline" => "other scenes in the timeline",
            _ => "the last step and deciding what to do next"
        };
    }

    private static string DescribeActionSummary(AgentEditActionDto action)
    {
        var kind = action.Action.Trim().ToLowerInvariant();
        return kind switch
        {
            "run_compliance_check" => "Run a compliance check on the current draft.",
            "finish" => "Finish editing the draft.",
            "read_section" => $"Read {DescribeParagraphRange(action)}.",
            "find_text" => $"Search for {OptionalQuote(action.Pattern ?? action.Query, "the target text")}.",
            "replace_text" => $"Replace {OptionalQuote(action.Pattern, "a pattern")}.",
            "swap_text" => "Swap the two selected excerpts.",
            "run_script" => $"Run a script with {action.Steps?.Count ?? 0} step(s).",
            "invoke_writer" => $"Invoke Writer on {DescribeParagraphRange(action)}.",
            "invoke_editor" => $"Invoke Editor on {DescribeParagraphRange(action)}.",
            "invoke_corrector" => $"Invoke Corrector on {DescribeParagraphRange(action)}.",
            _ => $"Run {FriendlyToolName(kind)}."
        };
    }

    /// <summary>Specific subject for the next orchestrator turn (stored after each tool completes).</summary>
    public static string BuildContextForNextTurn(
        string toolName,
        string toolMessage,
        AgentEditActionDto? action,
        AgentEditLoopState state)
    {
        return toolName switch
        {
            "invoke_writer" or "invoke_editor" or "invoke_corrector" when action is not null =>
                DescribeDelegatedContext(RoleDisplayName(toolName.Replace("invoke_", "")), action, state, toolMessage),
            "run_compliance_check" => SummarizeComplianceContext(toolMessage),
            "replace_text" when action is not null =>
                $"the replace of {OptionalQuote(action.Pattern, "a pattern")} with {OptionalQuote(action.Replacement, "new text")}",
            "swap_text" when action is not null =>
                $"the swap of {OptionalQuote(ResolveSwapA(action), "selection A")} with {OptionalQuote(ResolveSwapB(action), "selection B")}",
            "patch_text" when action is not null =>
                $"the patch ({action.Mode}) in {DescribeParagraphRange(action)}",
            "propose_patch" when action is not null =>
                $"the direct rewrite of {DescribeParagraphRange(action)}",
            "read_section" when action is not null =>
                DescribeParagraphRange(action),
            "find_text" when action is not null =>
                $"matches for {OptionalQuote(action.Pattern ?? action.Query, "the search pattern")}",
            "query_lore" when action is not null =>
                $"lore hits for {OptionalQuote(action.Query, action.Scope ?? "all scopes")}",
            "query_timeline" when action is not null =>
                string.IsNullOrWhiteSpace(action.Query)
                    ? $"timeline scenes ({action.When ?? "all"})"
                    : $"timeline scenes matching {OptionalQuote(action.Query, action.Query)}",
            _ => ExtractBriefContext(toolMessage)
        };
    }

    public static string DescribeAction(AgentEditActionDto action, IReadOnlyList<string> paragraphs, string? scriptStepLabel = null)
    {
        var prefix = string.IsNullOrWhiteSpace(scriptStepLabel) ? "Agent" : scriptStepLabel.Trim();
        var kind = action.Action.Trim().ToLowerInvariant();
        var purpose = DescribePurpose(action);

        return kind switch
        {
            "invoke_writer" => $"{prefix} is invoking the Writer to rework {DescribeTarget(action, paragraphs)}{purpose}.",
            "invoke_editor" => $"{prefix} is invoking the Editor to touch up {DescribeTarget(action, paragraphs)}{purpose}.",
            "invoke_corrector" => $"{prefix} is invoking the Corrector to fix {DescribeTarget(action, paragraphs)}{purpose}.",
            "replace_text" => previewOnly(action)
                ? $"{prefix} is previewing a replace of {OptionalQuote(action.Pattern, "a pattern")} with {OptionalQuote(action.Replacement, "new text")}."
                : $"{prefix} is replacing {OptionalQuote(action.Pattern, "a pattern")} with {OptionalQuote(action.Replacement, "new text")}.",
            "swap_text" => previewOnly(action)
                ? $"{prefix} is previewing a swap of {OptionalQuote(ResolveSwapA(action), "selection A")} with {OptionalQuote(ResolveSwapB(action), "selection B")}."
                : $"{prefix} is swapping {OptionalQuote(ResolveSwapA(action), "selection A")} with {OptionalQuote(ResolveSwapB(action), "selection B")}.",
            "patch_text" => DescribePatchAction(prefix, action),
            "propose_patch" => $"{prefix} is replacing text in {DescribeParagraphRange(action)}{purpose}.",
            "read_section" => $"{prefix} is reading {DescribeParagraphRange(action)}.",
            "find_text" => $"{prefix} is searching the draft for {OptionalQuote(action.Pattern ?? action.Query, "a pattern")}.",
            "query_lore" => $"{prefix} is searching lore for {OptionalQuote(action.Query, "keywords")}.",
            "query_timeline" => string.IsNullOrWhiteSpace(action.Query)
                ? $"{prefix} is listing {TimelineScopeLabel(action.When)} scenes."
                : $"{prefix} is searching {TimelineScopeLabel(action.When)} for {OptionalQuote(action.Query, "keywords")}.",
            "run_compliance_check" => $"{prefix} is running a compliance check on the draft.",
            "run_script" => $"{prefix} is running a {action.Steps?.Count ?? 0}-step script{(string.IsNullOrWhiteSpace(action.Reason) ? "" : $" ({action.Reason.Trim()})")}.",
            "finish" => $"{prefix} is finishing{(string.IsNullOrWhiteSpace(action.Reason) ? "" : $": {TruncatePlain(action.Reason, PurposeMaxChars)}")}.",
            _ => $"{prefix} chose {kind}."
        };
    }

    public static string DescribeDelegatedResponse(string role, string replacement) =>
        $"{role} responded with {OptionalQuote(ExtractLeadExcerpt(replacement), "replacement prose")}.";

    public static string DescribeApplyingReplace(string originalSpan, string newText, int paragraphStart, int paragraphEnd)
    {
        var range = paragraphStart == paragraphEnd
            ? $"paragraph {paragraphStart}"
            : $"paragraphs {paragraphStart}..{paragraphEnd}";
        return $"Agent is replacing {OptionalQuote(ExtractLeadExcerpt(originalSpan), "the original passage")} with {OptionalQuote(ExtractLeadExcerpt(newText), "the new passage")} in {range}.";
    }

    public static string DescribeResult(
        string toolName,
        AgentToolExecuteStatus status,
        string message,
        AgentEditActionDto? action = null)
    {
        if (status == AgentToolExecuteStatus.Finished)
            return message.StartsWith("Editor finished", StringComparison.Ordinal)
                ? message
                : "Agent finished editing the draft.";

        if (status == AgentToolExecuteStatus.Error || message.TrimStart().StartsWith("Error:", StringComparison.Ordinal))
            return $"That step failed: {ExtractErrorSummary(message)}";

        return toolName switch
        {
            "invoke_writer" => DescribeDelegatedResult("Writer", action, message),
            "invoke_editor" => DescribeDelegatedResult("Editor", action, message),
            "invoke_corrector" => DescribeDelegatedResult("Corrector", action, message),
            "replace_text" => SummarizeReplaceResult(message, action),
            "swap_text" => SummarizeSwapResult(message, action),
            "patch_text" => SummarizePatchResult(message),
            "propose_patch" => "Agent applied its direct patch to the draft.",
            "read_section" => action is null
                ? "Agent finished reading the requested section."
                : $"Agent finished reading {DescribeParagraphRange(action)}.",
            "find_text" => SummarizeFindResult(message, action),
            "run_compliance_check" => SummarizeComplianceResult(message),
            "run_script" => message.StartsWith("run_script", StringComparison.Ordinal)
                ? "Script completed."
                : "Script finished with an error.",
            "query_lore" or "query_timeline" => "Search results returned.",
            "finish" => message,
            _ => FriendlyToolName(toolName) + " completed."
        };
    }

    public static string RoleDisplayName(string kind) =>
        kind.ToLowerInvariant() switch
        {
            "writer" or "invoke_writer" => "Writer",
            "editor" or "invoke_editor" => "Editor",
            "corrector" or "invoke_corrector" => "Corrector",
            _ => kind
        };

    private static string DescribeDelegatedContext(string role, AgentEditActionDto? action, AgentEditLoopState state, string? replacementOrMessage = null)
    {
        if (action is null)
            return $"{role}'s rewrite";

        var excerpt = FirstNonEmpty(
            action.FocusExcerpt,
            action.Replacement is { } repl ? ExtractLeadExcerpt(repl) : null,
            replacementOrMessage is { } msg ? ExtractLeadExcerpt(msg) : null);
        var quoted = OptionalQuote(excerpt, DescribeParagraphRange(action));
        return $"{role}'s rewrite of {quoted}";
    }

    private static string DescribeDelegatedResult(string role, AgentEditActionDto? action, string message)
    {
        if (action?.Replacement is { } repl && !string.IsNullOrWhiteSpace(repl))
            return $"{role} rewrite applied: {OptionalQuote(ExtractLeadExcerpt(repl), "new prose")}.";
        return $"{role} rewrite applied to the draft.";
    }

    private static string DescribeTarget(AgentEditActionDto action, IReadOnlyList<string> paragraphs)
    {
        if (!string.IsNullOrWhiteSpace(action.FocusExcerpt))
            return QuoteForLog(action.FocusExcerpt);

        if (action.ParagraphStart is { } ps && action.ParagraphEnd is { } pe
            && ps >= 0 && pe >= ps && pe < paragraphs.Count)
        {
            var span = AgenticEditLoop.JoinParagraphs(paragraphs.Skip(ps).Take(pe - ps + 1).ToList());
            var excerpt = ExtractLeadExcerpt(span);
            if (!string.IsNullOrWhiteSpace(excerpt))
                return QuoteForLog(excerpt);
        }

        if (action.ParagraphStart is { } p)
            return DescribeParagraphRange(action);

        return "the selected passage";
    }

    private static string DescribeParagraphRange(AgentEditActionDto action)
    {
        if (action.ParagraphStart is not { } ps)
            return "a paragraph range";
        if (action.ParagraphEnd is not { } pe || pe == ps)
            return $"paragraph {ps}";
        return $"paragraphs {ps}..{pe}";
    }

    private static string DescribePurpose(AgentEditActionDto action)
    {
        var purpose = FirstNonEmpty(action.Instruction, action.Reason, action.ComplianceNotes);
        if (string.IsNullOrWhiteSpace(purpose))
            return "";
        return $" to fix {OptionalQuote(purpose, "the cited issue", PurposeMaxChars)}";
    }

    private static string DescribePatchAction(string prefix, AgentEditActionDto action)
    {
        var mode = (action.Mode ?? "").Trim().ToLowerInvariant();
        var excerpt = OptionalQuote(action.Excerpt ?? action.Pattern, "the target excerpt");
        var text = OptionalQuote(action.Text ?? action.Replacement, "new text");
        var range = DescribeParagraphRange(action);
        return mode switch
        {
            "replace_excerpt" => $"{prefix} is replacing excerpt {excerpt} with {text} in {range}.",
            "remove_excerpt" => $"{prefix} is removing excerpt {excerpt} from {range}.",
            "insert_before_excerpt" => $"{prefix} is inserting {text} before {excerpt} in {range}.",
            "insert_after_excerpt" => $"{prefix} is inserting {text} after {excerpt} in {range}.",
            "append_paragraph" => $"{prefix} is appending {text} after {range}.",
            "prepend_paragraph" => $"{prefix} is prepending {text} before {range}.",
            _ => $"{prefix} is patching {range} ({mode})."
        };
    }

    private static string TimelineScopeLabel(string? when) =>
        (when ?? "all").Trim().ToLowerInvariant() switch
        {
            "before" => "earlier",
            "after" => "later",
            "current" => "current",
            _ => "timeline"
        };

    private static bool previewOnly(AgentEditActionDto action) => action.PreviewOnly == true;

    private static string SummarizeSwapResult(string message, AgentEditActionDto? action)
    {
        if (message.Contains("could not locate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("overlap", StringComparison.OrdinalIgnoreCase))
            return ExtractErrorSummary(message);
        if (message.Contains("preview", StringComparison.OrdinalIgnoreCase) && action is not null)
            return $"Swap preview: {OptionalQuote(ResolveSwapA(action), "selection A")} ↔ {OptionalQuote(ResolveSwapB(action), "selection B")}.";
        if (action is not null)
            return $"Swap applied: {OptionalQuote(ResolveSwapA(action), "selection A")} ↔ {OptionalQuote(ResolveSwapB(action), "selection B")}.";
        return "Swap applied to the draft.";
    }

    private static string? ResolveSwapA(AgentEditActionDto action) =>
        FirstNonEmpty(action.ExcerptA, action.Excerpt, action.Pattern);

    private static string? ResolveSwapB(AgentEditActionDto action) =>
        FirstNonEmpty(action.ExcerptB, action.Text, action.Replacement);

    private static string SummarizeReplaceResult(string message, AgentEditActionDto? action)
    {
        if (message.Contains("no matches", StringComparison.OrdinalIgnoreCase))
            return "Replace found no matches.";
        if (message.Contains("preview", StringComparison.OrdinalIgnoreCase))
            return "Replace preview completed (draft unchanged).";
        if (action is not null)
            return $"Replace applied: {OptionalQuote(action.Pattern, "pattern")} → {OptionalQuote(action.Replacement, "new text")}.";
        return "Replace applied to the draft.";
    }

    private static string SummarizePatchResult(string message) =>
        message.StartsWith("Error:", StringComparison.Ordinal) ? ExtractErrorSummary(message) : "Patch applied to the draft.";

    private static string SummarizeFindResult(string message, AgentEditActionDto? action)
    {
        if (message.Contains("(no matches)", StringComparison.Ordinal))
            return action is null
                ? "Search found no matches."
                : $"Search found no matches for {OptionalQuote(action.Pattern ?? action.Query, "the pattern")}.";
        var firstMatch = message.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("- ¶", StringComparison.Ordinal));
        if (firstMatch is not null)
            return $"Search found {firstMatch.TrimStart('-').Trim()}.";
        return "Search returned matches.";
    }

    private static string SummarizeComplianceResult(string message)
    {
        if (message.Contains("pass: true", StringComparison.OrdinalIgnoreCase))
            return "Compliance check passed.";
        var firstFix = message.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("- Fix:", StringComparison.Ordinal) || l.StartsWith("- ", StringComparison.Ordinal));
        if (firstFix is not null)
            return $"Compliance check failed — {firstFix.TrimStart('-').Trim()}.";
        if (message.Contains("pass: false", StringComparison.OrdinalIgnoreCase))
            return "Compliance check failed — fixes required.";
        return "Compliance check completed.";
    }

    private static string SummarizeComplianceContext(string message)
    {
        if (message.Contains("pass: true", StringComparison.OrdinalIgnoreCase))
            return "the passing compliance result";
        var firstFix = message.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("- Fix:", StringComparison.Ordinal));
        if (firstFix is not null)
            return firstFix["- Fix:".Length..].Trim();
        var firstViolation = message.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("- ", StringComparison.Ordinal) && !l.StartsWith("- Fix:", StringComparison.Ordinal));
        return firstViolation is not null
            ? firstViolation.TrimStart('-').Trim()
            : "the compliance failures";
    }

    private static string ExtractBriefContext(string message)
    {
        var line = message.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return string.IsNullOrWhiteSpace(line) ? "the last tool result" : TruncatePlain(line, 120);
    }

    private static string ExtractErrorSummary(string message)
    {
        var line = message.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("Error:", StringComparison.Ordinal))?.Trim()
                   ?? message.Split('\n').FirstOrDefault()?.Trim()
                   ?? message;
        return TruncatePlain(line, 160);
    }

    private static string ExtractLeadExcerpt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        var flat = CollapseWhitespace(text);
        var sentenceEnd = flat.IndexOfAny(['.', '!', '?']);
        if (sentenceEnd is >= 20 and <= QuoteMaxChars)
            return flat[..(sentenceEnd + 1)];
        return flat.Length <= QuoteMaxChars ? flat : flat[..QuoteMaxChars];
    }

    private static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static string TruncatePlain(string text, int max) =>
        text.Length <= max ? text : text[..max] + " (truncated)";

    private static string FriendlyToolName(string toolName) =>
        toolName.Replace('_', ' ');
}
