using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public class AgentDraftTextToolsTests
{
    [Fact]
    public void Find_literal_returns_paragraph_and_offset()
    {
        var paras = new List<string> { "Hello world.", "She were late." };
        var result = AgentDraftTextTools.Find(paras, "were", useRegex: false, caseSensitive: false, null, null, null);

        Assert.True(result.Ok);
        Assert.Single(result.Matches);
        Assert.Equal(1, result.Matches[0].ParagraphIndex);
        Assert.Contains(">>were<<", result.Matches[0].Excerpt);
    }

    [Fact]
    public void Replace_literal_updates_paragraph()
    {
        var paras = new List<string> { "She were late." };
        var result = AgentDraftTextTools.Replace(paras, "were", "was", useRegex: false, caseSensitive: false, null, null, null, previewOnly: false);

        Assert.True(result.Ok);
        Assert.Equal(1, result.ReplacementsApplied);
        Assert.Equal("She was late.", paras[0]);
    }

    [Fact]
    public void Replace_previewOnly_does_not_mutate()
    {
        var paras = new List<string> { "She were late." };
        var result = AgentDraftTextTools.Replace(paras, "were", "was", useRegex: false, caseSensitive: false, null, null, null, previewOnly: true);

        Assert.True(result.Ok);
        Assert.Equal("She were late.", paras[0]);
    }

    [Fact]
    public void Swap_same_paragraph_exchanges_two_phrases()
    {
        var paras = new List<string> { "First phrase. Second phrase." };
        var result = AgentDraftTextTools.Swap(paras, "First phrase.", "Second phrase.", useRegex: false, caseSensitive: true, null, null, previewOnly: false);

        Assert.True(result.Ok);
        Assert.Equal("Second phrase. First phrase.", paras[0]);
    }

    [Fact]
    public void Swap_cross_paragraph_exchanges_selections()
    {
        var paras = new List<string> { "Alpha here.", "Beta there." };
        var result = AgentDraftTextTools.Swap(paras, "Alpha here.", "Beta there.", useRegex: false, caseSensitive: true, null, null, previewOnly: false);

        Assert.True(result.Ok);
        Assert.Equal("Beta there.", paras[0]);
        Assert.Equal("Alpha here.", paras[1]);
    }

    [Fact]
    public void Swap_previewOnly_does_not_mutate()
    {
        var paras = new List<string> { "One. Two." };
        var result = AgentDraftTextTools.Swap(paras, "One.", "Two.", useRegex: false, caseSensitive: true, null, null, previewOnly: true);

        Assert.True(result.Ok);
        Assert.Equal("One. Two.", paras[0]);
    }
}
