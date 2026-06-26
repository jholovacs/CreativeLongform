using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Agent;

/// <summary>Lore and timeline catalogs shared by every agent session.</summary>
public sealed record AgentBookContext(AgentLoreCatalog Lore, AgentSceneContextCatalog Timeline);

/// <summary>Loads book-wide context for agent tools (<c>query_lore</c>, <c>query_timeline</c>).</summary>
public static class AgentBookContextLoader
{
    public static async Task<AgentBookContext> LoadAsync(
        ICreativeLongformDbContext db,
        Book book,
        Scene scene,
        IReadOnlyList<WorldElement> sceneWorldElements,
        IReadOnlyList<WorldElementLink> sceneScopedLinks,
        CancellationToken cancellationToken)
    {
        var bookElements = await db.WorldElements.AsNoTracking()
            .Where(e => e.BookId == book.Id)
            .ToListAsync(cancellationToken);
        var bookElementIds = bookElements.Select(e => e.Id).ToHashSet();
        var bookLinks = await db.WorldElementLinks.AsNoTracking()
            .Where(l => bookElementIds.Contains(l.FromWorldElementId) && bookElementIds.Contains(l.ToWorldElementId))
            .ToListAsync(cancellationToken);
        var bookScenes = await db.Scenes.AsNoTracking()
            .Where(s => s.Chapter.BookId == book.Id)
            .Include(s => s.Chapter)
            .Include(s => s.TimelineEntry)
            .OrderBy(s => s.Chapter!.Order)
            .ThenBy(s => s.Order)
            .ToListAsync(cancellationToken);

        return new AgentBookContext(
            AgentLoreCatalog.Create(book, sceneWorldElements, sceneScopedLinks, bookElements, bookLinks),
            AgentSceneContextCatalog.Create(book, scene, bookScenes));
    }
}
