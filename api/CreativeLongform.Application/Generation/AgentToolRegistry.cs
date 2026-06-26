namespace CreativeLongform.Application.Generation;

/// <summary>Tool names, usage hints, and failure-budget messaging for the agent edit loop.</summary>
public static class AgentToolRegistry
{
    public const int DefaultMaxConsecutiveFailures = 5;
    public const int MaxScriptSteps = 12;

    public static IReadOnlyList<string> AllToolSummaries { get; } =
    [
        "read_section — { paragraphStart, paragraphEnd } read inclusive ¶ range",
        "find_text — { pattern, useRegex?, caseSensitive?, paragraphStart?, paragraphEnd?, maxMatches? }",
        "replace_text — { pattern, replacement, useRegex?, caseSensitive?, paragraphStart?, paragraphEnd?, maxReplacements?, previewOnly? }",
        "swap_text — { excerptA, excerptB, useRegex?, caseSensitive?, paragraphStart?, paragraphEnd?, previewOnly? } exchange two located selections (aliases: excerpt+text)",
        "patch_text — { mode, paragraphStart, paragraphEnd?, excerpt?, text?, useRegex?, caseSensitive? } surgical insert/remove/replace within ¶s",
        "query_lore — { query, scope: scene|book|relationships|all }",
        "query_timeline — { query?, when: before|after|all|current } other scenes in story order",
        "run_compliance_check — (no args) compliance on current draft",
        "invoke_writer | invoke_editor | invoke_corrector — { paragraphStart, paragraphEnd, instruction, focusExcerpt?, contextParagraphsBefore?, contextParagraphsAfter?, complianceNotes?, reason? }",
        "propose_patch — { paragraphStart, paragraphEnd, replacement, reason? }",
        "run_script — { steps: [ {...tool json...}, ... ], reason? } up to 12 steps, stops on first error",
        "finish — { reason } only after run_compliance_check pass:true"
    ];

    public static string FormatAvailableTools() =>
        "Available tools:\n  - " + string.Join("\n  - ", AllToolSummaries);

    public static bool IsKnownAction(string action) =>
        action switch
        {
            "read_section" or "find_text" or "replace_text" or "swap_text" or "patch_text" or "query_lore" or "query_timeline"
                or "run_compliance_check" or "invoke_writer" or "invoke_editor" or "invoke_corrector"
                or "propose_patch" or "run_script" or "finish" => true,
            _ => false
        };

    public static string UnknownToolMessage(string action) =>
        $"Error: unknown action \"{action}\".\n{FormatAvailableTools()}\nRetry with one of the actions above.";

    /// <summary>Returns misuse hint if required fields missing; null if OK.</summary>
    public static string? ValidateToolUse(string action, AgentEditActionDto dto, int paragraphCount)
    {
        switch (action)
        {
            case "read_section":
                if (dto.ParagraphStart is null || dto.ParagraphEnd is null)
                    return "Error: read_section requires paragraphStart and paragraphEnd (inclusive indices 0..N-1).";
                break;
            case "find_text":
                if (string.IsNullOrWhiteSpace(dto.Pattern) && string.IsNullOrWhiteSpace(dto.Query))
                    return "Error: find_text requires \"pattern\" (or \"query\") — literal or regex string to search.";
                break;
            case "replace_text":
                if (string.IsNullOrWhiteSpace(dto.Pattern))
                    return "Error: replace_text requires \"pattern\" and \"replacement\". Use previewOnly:true to dry-run.";
                break;
            case "swap_text":
                return ValidateSwapText(dto);
            case "patch_text":
                return ValidatePatchText(dto);
            case "query_lore":
                if (string.IsNullOrWhiteSpace(dto.Query))
                    return "Error: query_lore requires \"query\" keywords. Optional scope: scene|book|relationships|all.";
                break;
            case "query_timeline":
                break;
            case "invoke_writer":
            case "invoke_editor":
            case "invoke_corrector":
                if (dto.ParagraphStart is null || dto.ParagraphEnd is null)
                    return $"Error: {action} requires paragraphStart, paragraphEnd, and non-empty \"instruction\".";
                if (string.IsNullOrWhiteSpace(dto.Instruction))
                    return $"Error: {action} requires \"instruction\" describing the surgical fix. Optional: focusExcerpt, contextParagraphsBefore/After.";
                break;
            case "propose_patch":
                if (dto.ParagraphStart is null || dto.ParagraphEnd is null || string.IsNullOrWhiteSpace(dto.Replacement))
                    return "Error: propose_patch requires paragraphStart, paragraphEnd, and replacement prose.";
                break;
            case "run_script":
                if (dto.Steps is null || dto.Steps.Count == 0)
                    return "Error: run_script requires \"steps\": [ array of tool JSON objects ]. Max 12 steps; stops on first error.";
                if (dto.Steps.Count > MaxScriptSteps)
                    return $"Error: run_script limited to {MaxScriptSteps} steps — split into multiple scripts.";
                break;
        }

        if (dto.ParagraphStart is { } ps && dto.ParagraphEnd is { } pe && paragraphCount > 0)
        {
            if (ps < 0 || pe < ps || pe >= paragraphCount)
                return $"Error: invalid paragraph range {ps}..{pe} for draft with {paragraphCount} paragraphs (0..{paragraphCount - 1}).";
        }

        return null;
    }

    private static string? ValidateSwapText(AgentEditActionDto dto)
    {
        var a = FirstNonEmpty(dto.ExcerptA, dto.Excerpt, dto.Pattern);
        var b = FirstNonEmpty(dto.ExcerptB, dto.Text, dto.Replacement);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return "Error: swap_text requires two selections — excerptA and excerptB (or excerpt + text / pattern + replacement). Use find_text first to locate unique excerpts.";
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static string? ValidatePatchText(AgentEditActionDto dto)
    {
        var mode = (dto.Mode ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(mode))
            return "Error: patch_text requires \"mode\": replace_excerpt | remove_excerpt | insert_before_excerpt | insert_after_excerpt | append_paragraph | prepend_paragraph.";
        if (dto.ParagraphStart is null)
            return "Error: patch_text requires paragraphStart (and usually excerpt or text depending on mode).";

        if (mode is "replace_excerpt" or "remove_excerpt" or "insert_before_excerpt" or "insert_after_excerpt"
            && string.IsNullOrWhiteSpace(dto.Excerpt) && string.IsNullOrWhiteSpace(dto.Pattern))
            return "Error: patch_text modes replace/remove/insert_* require \"excerpt\" (unique text to locate) or \"pattern\".";

        var payload = dto.Text ?? dto.Replacement;
        if (mode is "replace_excerpt" or "insert_before_excerpt" or "insert_after_excerpt"
            && string.IsNullOrWhiteSpace(payload))
            return "Error: patch_text requires \"text\" (or \"replacement\") for this mode.";

        if (mode is "append_paragraph" or "prepend_paragraph" && string.IsNullOrWhiteSpace(payload))
            return "Error: append_paragraph/prepend_paragraph require \"text\" to insert.";

        if (mode is not "replace_excerpt" and not "remove_excerpt" and not "insert_before_excerpt"
            and not "insert_after_excerpt" and not "append_paragraph" and not "prepend_paragraph")
            return "Error: patch_text mode must be replace_excerpt, remove_excerpt, insert_before_excerpt, insert_after_excerpt, append_paragraph, or prepend_paragraph.";

        return null;
    }

    public static string AppendFailureBudget(string message, int consecutiveFailures, int maxFailures) =>
        $"{message.TrimEnd()}\n(consecutive tool failures: {consecutiveFailures}/{maxFailures} — loop aborts at {maxFailures}.)";

    public static bool IsErrorResult(string? result) =>
        !string.IsNullOrEmpty(result) && result.TrimStart().StartsWith("Error:", StringComparison.Ordinal);
}

/// <summary>JSON action DTO shared by the agent loop and script steps.</summary>
public sealed class AgentEditActionDto
{
    public string Action { get; set; } = "";
    public int? ParagraphStart { get; set; }
    public int? ParagraphEnd { get; set; }
    public string? Replacement { get; set; }
    public string? Reason { get; set; }
    public string? Query { get; set; }
    public string? Scope { get; set; }
    public string? Instruction { get; set; }
    public string? ComplianceNotes { get; set; }
    public string? Pattern { get; set; }
    public bool? UseRegex { get; set; }
    public bool? CaseSensitive { get; set; }
    public int? MaxMatches { get; set; }
    public int? MaxReplacements { get; set; }
    public bool? PreviewOnly { get; set; }
    public string? FocusExcerpt { get; set; }
    public int? ContextParagraphsBefore { get; set; }
    public int? ContextParagraphsAfter { get; set; }
    public string? Mode { get; set; }
    public string? Excerpt { get; set; }
    public string? Text { get; set; }
    public string? ExcerptA { get; set; }
    public string? ExcerptB { get; set; }
    public string? When { get; set; }
    public List<AgentEditActionDto>? Steps { get; set; }
}
