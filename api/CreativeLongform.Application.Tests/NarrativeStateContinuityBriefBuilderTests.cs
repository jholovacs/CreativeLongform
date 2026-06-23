using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Narrative;

namespace CreativeLongform.Application.Tests;

public sealed class NarrativeStateContinuityBriefBuilderTests
{
    [Fact]
    public void BuildForDraftPrompt_omits_pose_clothing_and_emotional_fields()
    {
        const string json =
            """
            {"schemaVersion":1,"characters":[{"name":"Mara","pose":"leaning on the counter","clothing":"blue apron over grey sweater","emotionalState":"anxious and watchful","topOfMind":["the forged letter"]}],"environment":{"setting":"kitchen","timeOfDay":"morning"}}
            """;

        var brief = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(json);

        Assert.Contains("Mara", brief);
        Assert.Contains("kitchen", brief, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forged letter", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blue apron", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("anxious", brief, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaning on the counter", brief, StringComparison.OrdinalIgnoreCase);
    }
}
