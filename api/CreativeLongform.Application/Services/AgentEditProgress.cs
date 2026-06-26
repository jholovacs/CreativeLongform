using System.Text;
using System.Text.Json;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Services;

/// <summary>Surfaces agent tool choices and outcomes to the generation event log (SignalR).</summary>
internal static class AgentEditProgress
{
    public const int EventLogDetailMaxChars = 16_000;
    private const int ActionStringFieldMaxChars = 1_500;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Task NotifyStatusAsync(
        AgentEditLoopState state,
        string narrative,
        Guid? llmCallId = null,
        long stepDurationMs = 0) =>
        NotifyAsync(state, "AgentEditStatus", "status", narrative, stepDurationMs, llmCallId ?? Guid.Empty);

    public static Task NotifyActionAsync(
        AgentEditLoopState state,
        int turn,
        int maxTurns,
        AgentEditActionDto action,
        Guid llmCallId,
        long stepDurationMs,
        string? scriptStepLabel = null)
    {
        var narrative = AgentEditNarrative.DescribeAction(action, state.Paragraphs, scriptStepLabel);
        var reflection = AgentEditNarrative.DescribeReflection(action);
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(reflection))
        {
            sb.AppendLine(reflection);
            sb.AppendLine();
        }

        sb.AppendLine(narrative);
        sb.AppendLine();
        sb.AppendLine($"Turn {turn}/{maxTurns} · action: {action.Action.Trim()}");
        if (!string.IsNullOrWhiteSpace(action.Reason))
            sb.AppendLine($"Reason: {action.Reason.Trim()}");
        sb.AppendLine("Action JSON:");
        sb.Append(FormatActionJson(action));
        return NotifyAsync(state, "AgentEditAction", action.Action.Trim(), sb.ToString().TrimEnd(), stepDurationMs, llmCallId);
    }

    public static Task NotifyActionAttemptAsync(
        AgentEditLoopState state,
        int turn,
        int maxTurns,
        string rawModelOutput,
        Guid llmCallId,
        long stepDurationMs,
        string summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Agent could not parse the orchestrator response — {summary.ToLowerInvariant()}");
        sb.AppendLine();
        sb.AppendLine($"Turn {turn}/{maxTurns}");
        sb.AppendLine("Raw model output (truncated):");
        sb.Append(AgenticEditLoop.Truncate(rawModelOutput, EventLogDetailMaxChars));
        return NotifyAsync(state, "AgentEditAction", "parse_error", sb.ToString().TrimEnd(), stepDurationMs, llmCallId);
    }

    public static Task NotifyResultAsync(
        AgentEditLoopState state,
        int turn,
        int maxTurns,
        string toolName,
        AgentToolExecuteStatus status,
        string message,
        Guid llmCallId,
        long stepDurationMs,
        AgentEditActionDto? action = null,
        string? scriptStepLabel = null)
    {
        var narrative = AgentEditNarrative.DescribeResult(toolName, status, message, action);
        var sb = new StringBuilder();
        sb.AppendLine(narrative);
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(scriptStepLabel)
            ? $"Turn {turn}/{maxTurns} · tool: {toolName}"
            : $"{scriptStepLabel} · tool: {toolName}");
        sb.AppendLine("Tool response:");
        sb.Append(AgenticEditLoop.Truncate(message, EventLogDetailMaxChars));
        return NotifyAsync(state, "AgentEditResult", toolName, sb.ToString().TrimEnd(), stepDurationMs, llmCallId);
    }

    public static string FormatActionJson(AgentEditActionDto action) =>
        JsonSerializer.Serialize(SanitizeForLog(action), JsonOptions);

    private static AgentEditActionDto SanitizeForLog(AgentEditActionDto action)
    {
        var copy = new AgentEditActionDto
        {
            Action = action.Action,
            ParagraphStart = action.ParagraphStart,
            ParagraphEnd = action.ParagraphEnd,
            Replacement = TruncateField(action.Replacement),
            Reason = TruncateField(action.Reason),
            Conclusion = TruncateField(action.Conclusion),
            NextStep = TruncateField(action.NextStep),
            Query = TruncateField(action.Query),
            Scope = action.Scope,
            Instruction = TruncateField(action.Instruction),
            ComplianceNotes = TruncateField(action.ComplianceNotes),
            Pattern = TruncateField(action.Pattern),
            UseRegex = action.UseRegex,
            CaseSensitive = action.CaseSensitive,
            MaxMatches = action.MaxMatches,
            MaxReplacements = action.MaxReplacements,
            PreviewOnly = action.PreviewOnly,
            FocusExcerpt = TruncateField(action.FocusExcerpt),
            ContextParagraphsBefore = action.ContextParagraphsBefore,
            ContextParagraphsAfter = action.ContextParagraphsAfter,
            Mode = action.Mode,
            Excerpt = TruncateField(action.Excerpt),
            ExcerptA = TruncateField(action.ExcerptA),
            ExcerptB = TruncateField(action.ExcerptB),
            Text = TruncateField(action.Text),
            When = action.When,
            Steps = action.Steps?.Select(SanitizeForLog).ToList()
        };
        return copy;
    }

    private static string? TruncateField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        return value.Length <= ActionStringFieldMaxChars
            ? value
            : value[..ActionStringFieldMaxChars] + "…";
    }

    private static Task NotifyAsync(
        AgentEditLoopState state,
        string eventName,
        string toolStep,
        string detail,
        long stepDurationMs,
        Guid llmCallId) =>
        state.Notifier.NotifyAsync(
            state.RunId,
            eventName,
            toolStep,
            detail,
            state.CancellationToken,
            state.PipelineElapsedMs(),
            stepDurationMs,
            llmCallId == Guid.Empty ? null : llmCallId);
}
