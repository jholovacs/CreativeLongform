using CreativeLongform.Application.Agent;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class AgentEnhancementTests
{
    [Fact]
    public void SceneBriefChecker_finds_missing_beat_keywords()
    {
        var result = AgentSceneBriefChecker.Run(
            "Mara walked into the tavern.",
            "Mara confronts the guild master about the stolen treaty.",
            "Ends with Mara leaving angry.");
        Assert.Contains("check_scene_brief", result, StringComparison.Ordinal);
        Assert.Contains("review", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FixInstructionPrioritizer_orders_instruction_violations_first()
    {
        var ordered = AgentFixInstructionPrioritizer.OrderCompliance(new ComplianceVerdict
        {
            Pass = false,
            Violations = ["Grammar in ¶2", "Wrong ending vs scene instructions"],
            FixInstructions = ["Fix comma splice", "Rewrite ending to match synopsis"]
        });
        Assert.Contains("ending", ordered.Violations[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ending", ordered.FixInstructions[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EditDiff_shows_before_and_after()
    {
        var diff = AgentEditDiff.Format("Old prose here.", "New prose here.");
        Assert.Contains("before:", diff, StringComparison.Ordinal);
        Assert.Contains("after:", diff, StringComparison.Ordinal);
    }

    [Fact]
    public void DelegationVerifier_warns_on_identical_output()
    {
        var msg = AgentDelegationVerifier.Assess("Convert to past tense", "Same text.", "Same text.");
        Assert.Contains("identical", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SessionBudget_scales_turns_and_checks()
    {
        Assert.Equal(16, AgentSessionBudget.ScaleTurns(16, 4));
        Assert.True(AgentSessionBudget.ScaleChecks(8, 30) > 8);
    }
}
