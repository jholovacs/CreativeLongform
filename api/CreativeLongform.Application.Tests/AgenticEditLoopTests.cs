using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreativeLongform.Application.Tests;

public class AgenticEditLoopTests
{
    [Fact]
    public void SplitParagraphs_splits_on_blank_lines()
    {
        var p = AgenticEditLoop.SplitParagraphs("One.\n\nTwo.\n\nThree.");
        Assert.Equal(3, p.Count);
        Assert.Equal("One.", p[0]);
        Assert.Equal("Two.", p[1]);
        Assert.Equal("Three.", p[2]);
    }

    [Fact]
    public void SplitParagraphs_normalizes_crlf()
    {
        var p = AgenticEditLoop.SplitParagraphs("A\r\n\r\nB");
        Assert.Equal(2, p.Count);
    }

    [Fact]
    public void JoinParagraphs_joins_with_double_newline()
    {
        var s = AgenticEditLoop.JoinParagraphs(["a", "b"]);
        Assert.Equal("a\n\nb", s);
    }

    [Fact]
    public void ApplyPatch_replaces_inclusive_range()
    {
        var list = new List<string> { "p0", "p1", "p2" };
        AgenticEditLoop.ApplyPatch(list, 1, 1, "new1");
        Assert.Equal(["p0", "new1", "p2"], list);
    }

    [Fact]
    public void ApplyPatch_replacement_can_introduce_multiple_paragraphs()
    {
        var list = new List<string> { "a", "b" };
        AgenticEditLoop.ApplyPatch(list, 0, 0, "x\n\ny");
        Assert.Equal(["x", "y", "b"], list);
    }

    [Fact]
    public async Task RunAsync_finish_returns_draft_unchanged()
    {
        var draft = "First.\n\nSecond.";
        var result = await AgenticEditLoop.RunAsync(
            draft,
            sceneInstructions: "Write scene.",
            expectedEndNotes: null,
            worldBlock: "(none)",
            maxTurns: 3,
            NullLogger.Instance,
            (_, _, _) => Task.FromResult(("""{"action":"finish","reason":"ok"}""", "")),
            new NoopNotifier(),
            Guid.NewGuid(),
            () => 0L,
            CancellationToken.None);

        Assert.Equal(draft.Replace("\r\n", "\n", StringComparison.Ordinal), result.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_propose_patch_then_finish_updates_text()
    {
        var calls = 0;
        var result = await AgenticEditLoop.RunAsync(
            "Old.\n\nKeep.",
            "instr",
            null,
            "world",
            maxTurns: 5,
            NullLogger.Instance,
            (_, _, _) =>
            {
                calls++;
                return Task.FromResult(calls switch
                {
                    1 => ("""{"action":"propose_patch","paragraphStart":0,"paragraphEnd":0,"replacement":"New."}""", ""),
                    _ => ("""{"action":"finish","reason":"done"}""", "")
                });
            },
            new NoopNotifier(),
            Guid.NewGuid(),
            () => 0L,
            CancellationToken.None);

        Assert.Contains("New.", result, StringComparison.Ordinal);
        Assert.Contains("Keep.", result, StringComparison.Ordinal);
        Assert.Equal(2, calls);
    }

    private sealed class NoopNotifier : IGenerationProgressNotifier
    {
        public Task NotifyAsync(Guid generationRunId, string eventName, string? step, string? detail,
            CancellationToken cancellationToken = default, long? elapsedMsSinceRunStart = null,
            long? stepDurationMs = null, string? llmResponsePreview = null, string? llmRequestPayload = null) =>
            Task.CompletedTask;
    }
}
