using CreativeLongform.Application.Agent;

namespace CreativeLongform.Application.Tests;

public sealed class AgentWordBudgetTests
{
    [Fact]
    public void Analyze_short_draft_recommends_break_up()
    {
        var draft = string.Join(' ', Enumerable.Repeat("word", 200));
        var a = AgentWordBudget.Analyze(draft, minWords: 1500, maxWords: 2000, paragraphCount: 4);

        Assert.True(a.Deficit > 0);
        Assert.True(a.NeedsBreakUp);
        Assert.InRange(a.SuggestedBeatCount, 2, 8);
    }

    [Fact]
    public void Analyze_at_minimum_does_not_need_break_up()
    {
        var draft = string.Join(' ', Enumerable.Repeat("word", 1600));
        var a = AgentWordBudget.Analyze(draft, minWords: 1500, maxWords: 2000, paragraphCount: 6);

        Assert.Equal(0, a.Deficit);
        Assert.False(a.NeedsBreakUp);
        Assert.Equal(0, a.SuggestedBeatCount);
    }

    [Fact]
    public void BuildWriterBeatInstruction_includes_target_when_set()
    {
        var s = AgentWordBudget.BuildWriterBeatInstruction("Add confrontation.", 350);
        Assert.Contains("350", s);
        Assert.Contains("confrontation", s, StringComparison.OrdinalIgnoreCase);
    }
}
