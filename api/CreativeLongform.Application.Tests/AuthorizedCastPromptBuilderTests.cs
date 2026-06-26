using CreativeLongform.Application.Narrative;
using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

public sealed class AuthorizedCastPromptBuilderTests
{
    [Fact]
    public void Build_lists_stateBefore_and_linked_characters()
    {
        var elements = new List<WorldElement>
        {
            new() { Id = Guid.NewGuid(), Kind = WorldElementKind.Character, Title = "Elena", Summary = "Healer" },
            new() { Id = Guid.NewGuid(), Kind = WorldElementKind.Geography, Title = "Harbor", Summary = "Port town" }
        };

        var block = AuthorizedCastPromptBuilder.Build(
            NarrativeStateTestFixtures.MaraAtKitchen,
            elements,
            "Mara meets a stranger at the dock.");

        Assert.Contains("Mara (stateBefore", block);
        Assert.Contains("Elena (linked world-building", block);
        Assert.DoesNotContain("Harbor", block);
        Assert.Contains("scene synopsis/instructions", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_deduplicates_same_name_from_multiple_sources()
    {
        var elements = new List<WorldElement>
        {
            new() { Id = Guid.NewGuid(), Kind = WorldElementKind.Character, Title = "Mara", Summary = "Protagonist" }
        };

        var block = AuthorizedCastPromptBuilder.Build(NarrativeStateTestFixtures.MaraAtKitchen, elements);

        Assert.Equal(1, block.Split("- Mara (", StringSplitOptions.None).Length - 1);
    }
}
