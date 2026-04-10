using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

/// <summary>
/// Unit tests for <see cref="WorldContextBuilder"/>, which assembles book tone, measurement baselines, linked world elements, and optional relationship links for LLM prompts.
/// </summary>
public class WorldContextBuilderTests
{
    /// <summary>
    /// <para><b>System under test:</b> <see cref="WorldContextBuilder.Build(CreativeLongform.Domain.Entities.Book,System.Collections.Generic.IReadOnlyList{CreativeLongform.Domain.Entities.WorldElement})"/> with no elements.</para>
    /// <para><b>Test case:</b> Book has tone and Earth metric preset; elements list empty.</para>
    /// <para><b>Expected result:</b> Tone and metric appear; empty-link messaging and standard authoring guardrails are present.</para>
    /// <para><b>Why it matters:</b> Baseline sections must always render so the model knows style and units even with no lore linked.</para>
    /// </summary>
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

    /// <summary>
    /// <para><b>System under test:</b> <see cref="WorldContextBuilder.Build"/> grouping and truncation.</para>
    /// <para><b>Test case:</b> Two elements of different kinds; one has an oversized <see cref="WorldElement.Detail"/>.</para>
    /// <para><b>Expected result:</b> Kind headers and status labels appear; long detail is truncated (ellipsis); output shorter than raw detail.</para>
    /// <para><b>Why it matters:</b> Prevents token blow-up and keeps readable ordering by kind; truncation must be visible to avoid silent cut mid-sentence without signal.</para>
    /// </summary>
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

    /// <summary>
    /// <para><b>System under test:</b> <see cref="WorldContextBuilder.Build"/> with explicit scene-scoped links.</para>
    /// <para><b>Test case:</b> Two canon elements and one <see cref="WorldElementLink"/> with label and detail.</para>
    /// <para><b>Expected result:</b> Relationship section lists both titles, relation label, and detail text.</para>
    /// <para><b>Why it matters:</b> Scene workflow passes scoped links so the model sees relationships between elements actually tied to the scene.</para>
    /// </summary>
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

    /// <summary>
    /// <para><b>System under test:</b> <see cref="WorldContextBuilder.Build"/> overload without links.</para>
    /// <para><b>Test case:</b> Elements present but two-argument overload (no links argument).</para>
    /// <para><b>Expected result:</b> No “Relationships between scene-linked elements” section.</para>
    /// <para><b>Why it matters:</b> Avoids implying cross-element relations when the caller did not supply scoped links.</para>
    /// </summary>
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

    /// <summary>
    /// <para><b>System under test:</b> <see cref="WorldContextBuilder.Build"/> with an empty link list.</para>
    /// <para><b>Test case:</b> Pass explicit empty collection for links.</para>
    /// <para><b>Expected result:</b> Same as no links — no relationship header.</para>
    /// <para><b>Why it matters:</b> Callers may always pass a list; empty must not emit a misleading relationship block.</para>
    /// </summary>
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
