using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Services;

namespace CreativeLongform.Application.Tests;

public class AgentEditNarrativeTests
{
    [Fact]
    public void DescribeAction_invoke_editor_includes_target_and_purpose()
    {
        var action = new AgentEditActionDto
        {
            Action = "invoke_editor",
            ParagraphStart = 1,
            ParagraphEnd = 1,
            FocusExcerpt = "He walks slowly.",
            Instruction = "Convert all verbs to past tense."
        };
        var msg = AgentEditNarrative.DescribeAction(action, ["She ran.", "He walks slowly."]);
        Assert.Contains("invoking the Editor", msg, StringComparison.Ordinal);
        Assert.Contains("'He walks slowly.'", msg, StringComparison.Ordinal);
        Assert.Contains("past tense", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeThinking_uses_specific_hint_when_set()
    {
        var state = new AgentEditLoopState
        {
            Paragraphs = ["a"],
            Notifier = new NoopNotifier(),
            RunId = Guid.NewGuid(),
            PipelineElapsedMs = () => 0,
            CancellationToken = CancellationToken.None,
            LastNarrativeHint = "Editor's rewrite of 'He walked slowly into the room.'"
        };
        var msg = AgentEditNarrative.DescribeThinking(state);
        Assert.Contains("He walked slowly into the room", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("…", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeThinking_includes_last_conclusion_when_set()
    {
        var state = new AgentEditLoopState
        {
            Paragraphs = ["a"],
            Notifier = new NoopNotifier(),
            RunId = Guid.NewGuid(),
            PipelineElapsedMs = () => 0,
            CancellationToken = CancellationToken.None,
            LastNarrativeHint = "compliance failures",
            LastConclusion = "Past tense errors remain in paragraph 1."
        };
        var msg = AgentEditNarrative.DescribeThinking(state);
        Assert.Contains("last conclusion:", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Past tense errors remain", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeReflection_formats_conclusion_and_next_step()
    {
        var action = new AgentEditActionDto
        {
            Action = "invoke_editor",
            Conclusion = "Compliance failed on tense in paragraph 1.",
            NextStep = "Invoke Editor on paragraph 1 to convert verbs to past tense.",
            ParagraphStart = 1,
            ParagraphEnd = 1
        };
        var msg = AgentEditNarrative.DescribeReflection(action);
        Assert.Contains("Agent concluded:", msg, StringComparison.Ordinal);
        Assert.Contains("Next step:", msg, StringComparison.Ordinal);
        Assert.Contains("past tense", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DescribeReflection_falls_back_to_reason_when_conclusion_missing()
    {
        var action = new AgentEditActionDto
        {
            Action = "run_compliance_check",
            Reason = "Need a fresh compliance verdict after the patch."
        };
        var msg = AgentEditNarrative.DescribeReflection(action);
        Assert.Contains("fresh compliance verdict", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuoteForLog_empty_returns_empty_not_ellipsis_placeholder()
    {
        Assert.Equal("", AgentEditNarrative.QuoteForLog(null));
        Assert.Equal("the pattern", AgentEditNarrative.OptionalQuote(null, "the pattern"));
    }

    [Fact]
    public void DescribeApplyingReplace_quotes_old_and_new()
    {
        var msg = AgentEditNarrative.DescribeApplyingReplace("Old text here.", "New text here.", 0, 0);
        Assert.Contains("replacing", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'Old text here.'", msg, StringComparison.Ordinal);
        Assert.Contains("'New text here.'", msg, StringComparison.Ordinal);
    }

    private sealed class NoopNotifier : IGenerationProgressNotifier
    {
        public Task NotifyAsync(Guid generationRunId, string eventName, string? step, string? detail,
            CancellationToken cancellationToken = default, long? elapsedMsSinceRunStart = null,
            long? stepDurationMs = null,         Guid? llmCallId = null,
        string? workingDocumentText = null,
        int? documentRevision = null) =>
            Task.CompletedTask;
    }
}
