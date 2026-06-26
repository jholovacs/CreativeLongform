using CreativeLongform.Application.Agent;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class AgentProposePatchGuardTests
{
    [Fact]
    public void Validate_rejects_substantive_prose_in_propose_patch()
    {
        var longReplacement = string.Join(' ', Enumerable.Repeat("word", 60));
        var err = AgentProposePatchGuard.Validate(new AgentEditActionDto
        {
            ParagraphStart = 0,
            ParagraphEnd = 0,
            Replacement = longReplacement
        });

        Assert.NotNull(err);
        Assert.Contains("invoke_writer", err, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_allows_short_micro_edit()
    {
        var err = AgentProposePatchGuard.Validate(new AgentEditActionDto
        {
            ParagraphStart = 2,
            ParagraphEnd = 2,
            Replacement = "She whispered, \"Not yet.\""
        });

        Assert.Null(err);
    }
}
