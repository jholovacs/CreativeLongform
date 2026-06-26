using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class LlmProseSanitizerTests
{
    /// <summary>Some providers wrap reasoning in this tag; build name at runtime so tooling does not strip it.</summary>
    private const string RedactedThinkingTag = "redacted_" + "thinking";

    [Fact]
    public void ProseForApplication_strips_thinking_tags()
    {
        const string raw = "<thinking>plan</thinking>Hello world.";
        Assert.Equal("Hello world.", LlmProseSanitizer.ProseForApplication(raw));
    }

    [Fact]
    public void SplitThinkingFromProse_single_line_redacted_tag()
    {
        var raw = $"<{RedactedThinkingTag}>Plan.</{RedactedThinkingTag}> She stepped into the rain.";
        var split = LlmProseSanitizer.SplitThinkingFromProse(raw);
        Assert.Equal("She stepped into the rain.", split.Prose);
        Assert.Equal("Plan.", split.ThinkingNotes);
    }

    [Fact]
    public void SplitThinkingFromProse_removes_redacted_thinking_and_keeps_prose()
    {
        var raw = $"""
            <{RedactedThinkingTag}>
            Plan the opening beat.
            </{RedactedThinkingTag}>

            She stepped into the rain.
            """;

        var split = LlmProseSanitizer.SplitThinkingFromProse(raw);

        Assert.Equal("She stepped into the rain.", split.Prose);
        Assert.Contains("Plan the opening beat.", split.ThinkingNotes);
    }

    [Fact]
    public void SplitThinkingFromProse_handles_generic_thinking_tag()
    {
        const string raw = "<thinking>notes</thinking>Hello.";

        var split = LlmProseSanitizer.SplitThinkingFromProse(raw);

        Assert.Equal("Hello.", split.Prose);
        Assert.Equal("notes", split.ThinkingNotes);
    }

    [Fact]
    public void SplitThinkingFromProse_returns_null_notes_when_none()
    {
        var split = LlmProseSanitizer.SplitThinkingFromProse("Plain prose only.");

        Assert.Equal("Plain prose only.", split.Prose);
        Assert.Null(split.ThinkingNotes);
    }
}
