using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

public class WorldContextBuilderTests
{
    [Fact]
    public void Build_empty_elements_returns_tone_and_measurement_baseline()
    {
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = "T",
            StoryToneAndStyle = "Grim",
            MeasurementPreset = MeasurementPreset.EarthMetric
        };
        var text = WorldContextBuilder.Build(book, []);
        Assert.Contains("Grim", text, StringComparison.Ordinal);
        Assert.Contains("metric", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Linked world-building", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_groups_elements_by_kind_and_truncates_long_detail()
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "B", MeasurementPreset = MeasurementPreset.Custom };
        var longDetail = new string('x', 2000);
        var elements = new List<WorldElement>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Kind = WorldElementKind.Lore,
                Title = "Alpha",
                Summary = "S1",
                Detail = "short",
                Status = WorldElementStatus.Canon,
                Provenance = WorldElementProvenance.Manual
            },
            new()
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Kind = WorldElementKind.Geography,
                Title = "Zed",
                Summary = "S2",
                Detail = longDetail,
                Status = WorldElementStatus.Draft,
                Provenance = WorldElementProvenance.Manual
            }
        };

        var text = WorldContextBuilder.Build(book, elements);
        Assert.Contains("[Geography]", text, StringComparison.Ordinal);
        Assert.Contains("[Lore]", text, StringComparison.Ordinal);
        Assert.Contains("canon", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draft", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.True(text.Length < longDetail.Length);
    }
}
