using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class ComplianceVerdictGroundingTests
{
    [Fact]
    public void GroundAgainstDraft_drops_prompt_echo_violations_and_phantom_fixes()
    {
        var draft = "Alex opened the door. The room was cold.";
        var raw = new ComplianceVerdict
        {
            Pass = false,
            Violations =
            [
                "Invented characters or relationships",
                "Plot events not grounded in the scene synopsis/instructions (below), stateBefore, and linked world-building — not in the book-level synopsis alone."
            ],
            FixInstructions =
            [
                "Change 'He were' to 'He was' in ¶1",
                "Add closing quote after 'said Mara'"
            ]
        };

        var result = ComplianceVerdictGrounding.GroundAgainstDraft(draft, raw);

        Assert.True(result.Verdict.Pass);
        Assert.Empty(result.Verdict.Violations);
        Assert.Empty(result.Verdict.FixInstructions);
        Assert.Equal(4, result.DroppedItems.Count);
    }

    [Fact]
    public void GroundAgainstDraft_keeps_fix_when_quoted_text_exists_in_draft()
    {
        var draft = "She walked slowly into the room.";
        var raw = new ComplianceVerdict
        {
            Pass = false,
            Violations = ["Grammar: subject-verb issue in quoted line"],
            FixInstructions = ["Change 'walked slowly' to 'walked slowly,' in ¶0"]
        };

        var result = ComplianceVerdictGrounding.GroundAgainstDraft(draft, raw);

        Assert.False(result.Verdict.Pass);
        Assert.Single(result.Verdict.FixInstructions);
        Assert.Contains("walked slowly", result.Verdict.FixInstructions[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractQuotedStrings_finds_single_and_double_quoted_phrases()
    {
        var quotes = ComplianceVerdictGrounding.ExtractQuotedStrings("Change 'He were' to \"He was\" in ¶1");
        Assert.Equal(2, quotes.Count);
        Assert.Contains("He were", quotes);
        Assert.Contains("He was", quotes);
    }
}
