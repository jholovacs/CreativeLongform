using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

public sealed class AgentLoreCatalogTests
{
    [Fact]
    public void Query_finds_scene_linked_character()
    {
        var bookId = Guid.NewGuid();
        var book = new Book { Id = bookId, Title = "T", Synopsis = "Epic fantasy" };
        var mara = new WorldElement
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            Kind = WorldElementKind.Character,
            Title = "Mara",
            Summary = "A cautious healer",
            Detail = "Scar on left hand"
        };
        var catalog = AgentLoreCatalog.Create(book, [mara], [], [mara], []);
        var result = catalog.Query("Mara", "scene");
        Assert.Contains("Mara", result, StringComparison.Ordinal);
        Assert.Contains("scene-linked", result, StringComparison.Ordinal);
        Assert.Contains("Scar on left hand", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Query_finds_relationship()
    {
        var bookId = Guid.NewGuid();
        var book = new Book { Id = bookId, Title = "T" };
        var a = new WorldElement { Id = Guid.NewGuid(), BookId = bookId, Kind = WorldElementKind.Character, Title = "A", Summary = "s" };
        var b = new WorldElement { Id = Guid.NewGuid(), BookId = bookId, Kind = WorldElementKind.Character, Title = "B", Summary = "s" };
        var link = new WorldElementLink
        {
            Id = Guid.NewGuid(),
            FromWorldElementId = a.Id,
            ToWorldElementId = b.Id,
            RelationLabel = "sibling",
            RelationDetail = "Raised together in the capital"
        };
        var catalog = AgentLoreCatalog.Create(book, [a, b], [link], [a, b], [link]);
        var result = catalog.Query("sibling", "relationships");
        Assert.Contains("A —sibling→ B", result, StringComparison.Ordinal);
        Assert.Contains("Raised together", result, StringComparison.Ordinal);
    }
}
