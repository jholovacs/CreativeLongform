using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class DraftProseGuardTests
{
    [Fact]
    public void TrimRepetitiveLoops_leaves_clean_prose_unchanged()
    {
        const string text = "She opened the door.\n\nRain hammered the glass.\n\nHe was already inside.";
        Assert.Equal(text, DraftProseGuard.TrimRepetitiveLoops(text));
    }

    [Fact]
    public void TrimRepetitiveLoops_removes_duplicate_paragraph_loop()
    {
        const string para = "Mara stepped onto the wet stones and felt the cold climb her boots.";
        var raw = $"{para}\n\nHe waited under the awning.\n\n{para}\n\n{para}";
        var trimmed = DraftProseGuard.TrimRepetitiveLoops(raw);
        Assert.Equal($"{para}\n\nHe waited under the awning.", trimmed);
    }

    [Fact]
    public void TrimRepetitiveLoops_removes_duplicate_suffix()
    {
        const string chunk = "The harbor smelled of tar and salt. Ships groaned against the pilings in the dark.";
        var raw = $"Opening beat.\n\n{chunk}{chunk}";
        var trimmed = DraftProseGuard.TrimRepetitiveLoops(raw);
        Assert.Equal($"Opening beat.\n\n{chunk}", trimmed);
    }

    [Fact]
    public void MergeDraftContinuation_appends_continuation_only()
    {
        const string draft = "She entered the room.";
        const string continuation = "The lamp flickered once and died.";
        Assert.Equal($"{draft}\n\n{continuation}", DraftProseGuard.MergeDraftContinuation(draft, continuation));
    }

    [Fact]
    public void MergeDraftContinuation_strips_echoed_paragraph_from_continuation()
    {
        const string draft = "First paragraph.\n\nSecond paragraph.";
        const string continuation = "Second paragraph.\n\nThird paragraph.";
        Assert.Equal("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.",
            DraftProseGuard.MergeDraftContinuation(draft, continuation));
    }

    [Fact]
    public void MergeDraftContinuation_accepts_full_rewrite_when_model_returns_whole_scene()
    {
        const string draft = "Old draft.";
        const string rewrite = "Old draft.\n\nNew material continues the scene.";
        Assert.Equal(rewrite, DraftProseGuard.MergeDraftContinuation(draft, rewrite));
    }

    [Fact]
    public void TrimOpeningStateRecitation_removes_inventory_opening_paragraph()
    {
        const string stateJson =
            """
            {"schemaVersion":1,"characters":[{"name":"Mara","clothing":"a faded blue apron over a grey sweater","emotionalState":"anxious and watchful"}],"environment":{"setting":"kitchen"}}
            """;
        var prose =
            "Mara stood in the kitchen wearing a faded blue apron over a grey sweater, anxious and watchful.\n\n" +
            "She turned when the back door clicked.";

        var trimmed = DraftProseGuard.TrimOpeningStateRecitation(prose, stateJson);

        Assert.Equal("She turned when the back door clicked.", trimmed);
    }
}
