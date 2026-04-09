using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/scenes/{sceneId:guid}")]
public sealed class SceneWorldElementsWriteController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public SceneWorldElementsWriteController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPut("world-elements")]
    public async Task<ActionResult> PutWorldElements(
        Guid sceneId,
        [FromBody] WorldElementsBody body,
        CancellationToken cancellationToken)
    {
        var scene = await _db.Scenes
            .Include(s => s.SceneWorldElements)
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            return NotFound();

        var ids = body.WorldElementIds ?? Array.Empty<Guid>();
        var bookId = await _db.Chapters.AsNoTracking()
            .Where(c => c.Id == scene.ChapterId)
            .Select(c => c.BookId)
            .FirstAsync(cancellationToken);

        var validIds = await _db.WorldElements
            .AsNoTracking()
            .Where(w => w.BookId == bookId && ids.Contains(w.Id))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);

        _db.SceneWorldElements.RemoveRange(scene.SceneWorldElements);
        foreach (var wid in validIds)
        {
            _db.SceneWorldElements.Add(new SceneWorldElement
            {
                SceneId = sceneId,
                WorldElementId = wid
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public sealed class WorldElementsBody
    {
        public Guid[]? WorldElementIds { get; set; }
    }
}
