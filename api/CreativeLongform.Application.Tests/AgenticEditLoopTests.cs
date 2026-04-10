using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CreativeLongform.Application.Tests;

/// <summary>
/// Unit tests for <see cref="AgenticEditLoop"/> paragraph utilities and the async agent loop that applies LLM JSON actions to draft prose.
/// </summary>
public class AgenticEditLoopTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.SplitParagraphs"/>.</para>
    /// <para><b>Test case:</b> Text with blank-line separators between three paragraphs.</para>
    /// <para><b>Expected result:</b> Three non-empty paragraph strings in order.</para>
    /// <para><b>Why it matters:</b> Patch indices are paragraph-based; wrong splitting misaligns LLM <c>paragraphStart</c>/<c>paragraphEnd</c> with user-visible prose.</para>
    /// </summary>
    [Fact]
    public void SplitParagraphs_splits_on_blank_lines()
    {
        var p = AgenticEditLoop.SplitParagraphs("One.\n\nTwo.\n\nThree.");
        Assert.Equal(3, p.Count);
        Assert.Equal("One.", p[0]);
        Assert.Equal("Two.", p[1]);
        Assert.Equal("Three.", p[2]);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.SplitParagraphs"/> line-ending normalization.</para>
    /// <para><b>Test case:</b> Windows-style <c>\r\n\r\n</c> between two paragraphs.</para>
    /// <para><b>Expected result:</b> Two paragraphs detected (same as <c>\n\n</c>).</para>
    /// <para><b>Why it matters:</b> Drafts may come from browsers or paste buffers with CRLF; inconsistent handling would shift paragraph counts on Windows.</para>
    /// </summary>
    [Fact]
    public void SplitParagraphs_normalizes_crlf()
    {
        var p = AgenticEditLoop.SplitParagraphs("A\r\n\r\nB");
        Assert.Equal(2, p.Count);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.JoinParagraphs"/>.</para>
    /// <para><b>Test case:</b> Join two short strings after a split.</para>
    /// <para><b>Expected result:</b> Double newline between segments (canonical storage shape).</para>
    /// <para><b>Why it matters:</b> Rejoin must round-trip with <see cref="AgenticEditLoop.SplitParagraphs"/> so applied patches do not merge paragraphs accidentally.</para>
    /// </summary>
    [Fact]
    public void JoinParagraphs_joins_with_double_newline()
    {
        var s = AgenticEditLoop.JoinParagraphs(["a", "b"]);
        Assert.Equal("a\n\nb", s);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.ApplyPatch"/>.</para>
    /// <para><b>Test case:</b> Replace paragraph index 1 only (inclusive range 1–1) with a new string.</para>
    /// <para><b>Expected result:</b> Middle paragraph updated; neighbors unchanged.</para>
    /// <para><b>Why it matters:</b> Off-by-one or non-inclusive ranges would corrupt unrelated paragraphs during agent edits.</para>
    /// </summary>
    [Fact]
    public void ApplyPatch_replaces_inclusive_range()
    {
        var list = new List<string> { "p0", "p1", "p2" };
        AgenticEditLoop.ApplyPatch(list, 1, 1, "new1");
        Assert.Equal(["p0", "new1", "p2"], list);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.ApplyPatch"/> when replacement contains embedded blank lines.</para>
    /// <para><b>Test case:</b> Replace first paragraph with text that splits into two paragraphs.</para>
    /// <para><b>Expected result:</b> List grows: replacement is re-split so multiple paragraphs insert at the range.</para>
    /// <para><b>Why it matters:</b> LLM may return multi-paragraph insertions; failing to split would store a single paragraph with fake internal newlines.</para>
    /// </summary>
    [Fact]
    public void ApplyPatch_replacement_can_introduce_multiple_paragraphs()
    {
        var list = new List<string> { "a", "b" };
        AgenticEditLoop.ApplyPatch(list, 0, 0, "x\n\ny");
        Assert.Equal(["x", "y", "b"], list);
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.RunAsync"/> with a stub LLM that immediately finishes.</para>
    /// <para><b>Test case:</b> First (and only) model response is <c>{"action":"finish"}</c>.</para>
    /// <para><b>Expected result:</b> Returned draft equals input after normalizing CRLF to LF.</para>
    /// <para><b>Why it matters:</b> A no-op finish must not alter text; regressions here would show phantom edits in the UI.</para>
    /// </summary>
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
            (_, _, _) => Task.FromResult(("""{"action":"finish","reason":"ok"}""", "", Guid.Empty)),
            new NoopNotifier(),
            Guid.NewGuid(),
            () => 0L,
            CancellationToken.None);

        Assert.Equal(draft.Replace("\r\n", "\n", StringComparison.Ordinal), result.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// <para><b>System under test:</b> <see cref="AgenticEditLoop.RunAsync"/> multi-turn stub (patch then finish).</para>
    /// <para><b>Test case:</b> Turn 1 proposes replacing paragraph 0; turn 2 finishes.</para>
    /// <para><b>Expected result:</b> Output contains new first paragraph and preserved second; exactly two LLM calls.</para>
    /// <para><b>Why it matters:</b> Validates the core edit loop wiring; failures mean agentic editing never applies patches or loops infinitely.</para>
    /// </summary>
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
                    1 => ("""{"action":"propose_patch","paragraphStart":0,"paragraphEnd":0,"replacement":"New."}""", "", Guid.Empty),
                    _ => ("""{"action":"finish","reason":"done"}""", "", Guid.Empty)
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
            long? stepDurationMs = null, Guid? llmCallId = null) =>
            Task.CompletedTask;
    }
}
