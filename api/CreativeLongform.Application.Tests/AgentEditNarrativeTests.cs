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
            long? stepDurationMs = null, Guid? llmCallId = null) =>
            Task.CompletedTask;
    }
}
