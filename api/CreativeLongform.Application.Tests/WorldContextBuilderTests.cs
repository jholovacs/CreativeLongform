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
        Assert.Contains("Linked world-building", text, StringComparison.Ordinal);
        Assert.Contains("no world elements are linked", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authoritative scope", text, StringComparison.Ordinal);
        Assert.Contains("show, don't tell", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_groups_elements_by_kind_and_truncates_long_detail()
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "B", MeasurementPreset = MeasurementPreset.Custom };
        var longDetail = new string('x', 8000);
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
        Assert.Contains("Authoritative scope", text, StringComparison.Ordinal);
        Assert.Contains("show, don't tell", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Geography]", text, StringComparison.Ordinal);
        Assert.Contains("[Lore]", text, StringComparison.Ordinal);
        Assert.Contains("canon", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draft", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("…", text, StringComparison.Ordinal);
        Assert.True(text.Length < longDetail.Length);
    }

    [Fact]
    public void Build_with_scoped_links_appends_relationship_section_with_label_and_detail()
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "B", MeasurementPreset = MeasurementPreset.Custom };
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var elements = new List<WorldElement>
        {
            new()
            {
                Id = idA,
                BookId = book.Id,
                Kind = WorldElementKind.Character,
                Title = "Mara",
                Summary = "S",
                Status = WorldElementStatus.Canon,
                Provenance = WorldElementProvenance.Manual
            },
            new()
            {
                Id = idB,
                BookId = book.Id,
                Kind = WorldElementKind.Geography,
                Title = "Harbor",
                Summary = "S2",
                Status = WorldElementStatus.Canon,
                Provenance = WorldElementProvenance.Manual
            }
        };
        var links = new List<WorldElementLink>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FromWorldElementId = idA,
                ToWorldElementId = idB,
                RelationLabel = "located_in",
                RelationDetail = "She keeps a boat there."
            }
        };

        var text = WorldContextBuilder.Build(book, elements, links);
        Assert.Contains("Relationships between scene-linked elements", text, StringComparison.Ordinal);
        Assert.Contains("Mara", text, StringComparison.Ordinal);
        Assert.Contains("Harbor", text, StringComparison.Ordinal);
        Assert.Contains("located_in", text, StringComparison.Ordinal);
        Assert.Contains("She keeps a boat there.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_without_scoped_links_has_no_relationship_header()
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "B", MeasurementPreset = MeasurementPreset.Custom };
        var elements = new List<WorldElement>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Kind = WorldElementKind.Lore,
                Title = "X",
                Summary = "S",
                Status = WorldElementStatus.Draft,
                Provenance = WorldElementProvenance.Manual
            }
        };
        var text = WorldContextBuilder.Build(book, elements);
        Assert.DoesNotContain("Relationships between scene-linked elements", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_with_empty_scoped_links_list_has_no_relationship_header()
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "B", MeasurementPreset = MeasurementPreset.Custom };
        var elements = new List<WorldElement>
        {
            new()
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Kind = WorldElementKind.Lore,
                Title = "X",
                Summary = "S",
                Status = WorldElementStatus.Draft,
                Provenance = WorldElementProvenance.Manual
            }
        };
        var text = WorldContextBuilder.Build(book, elements, []);
        Assert.DoesNotContain("Relationships between scene-linked elements", text, StringComparison.Ordinal);
    }
}
