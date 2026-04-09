using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class SceneWorkflowController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;
    private readonly IWorldBuildingService _worldBuilding;

    public SceneWorkflowController(ICreativeLongformDbContext db, IWorldBuildingService worldBuilding)
    {
        _db = db;
        _worldBuilding = worldBuilding;
    }

    [HttpPatch("scenes/{sceneId:guid}")]
    public async Task<ActionResult> PatchScene(Guid sceneId, [FromBody] PatchSceneBody body, CancellationToken cancellationToken)
    {
        var scene = await _db.Scenes.FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            return NotFound();

        if (body.Title is not null)
            scene.Title = body.Title;
        if (body.Synopsis is not null)
            scene.Synopsis = body.Synopsis;
        if (body.Instructions is not null)
            scene.Instructions = body.Instructions;
        if (body.ExpectedEndStateNotes is not null)
            scene.ExpectedEndStateNotes = string.IsNullOrWhiteSpace(body.ExpectedEndStateNotes) ? null : body.ExpectedEndStateNotes;
        if (body.NarrativePerspective is not null)
            scene.NarrativePerspective = string.IsNullOrWhiteSpace(body.NarrativePerspective) ? null : body.NarrativePerspective;
        if (body.NarrativeTense is not null)
            scene.NarrativeTense = string.IsNullOrWhiteSpace(body.NarrativeTense) ? null : body.NarrativeTense;
        if (body.BeginningStateJson is not null)
            scene.BeginningStateJson = string.IsNullOrWhiteSpace(body.BeginningStateJson) ? null : body.BeginningStateJson;

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("scenes/{sceneId:guid}/workflow-context")]
    public async Task<ActionResult<SceneWorkflowContextDto>> GetWorkflowContext(Guid sceneId,
        CancellationToken cancellationToken)
    {
        var scene = await _db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            return NotFound();

        var bookId = scene.Chapter.BookId;
        var orderedIds = await _db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .Where(s => s.Chapter.BookId == bookId)
            .OrderBy(s => s.Chapter.Order).ThenBy(s => s.Order)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var idx = orderedIds.IndexOf(sceneId);
        var hasPrev = idx > 0;
        string? prevEnd = null;
        if (hasPrev)
        {
            var prevSceneId = orderedIds[idx - 1];
            var prevRun = await _db.GenerationRuns.AsNoTracking()
                .Where(r => r.SceneId == prevSceneId && r.Status == GenerationRunStatus.Succeeded)
                .OrderByDescending(r => r.CompletedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (prevRun is not null)
            {
                var snap = await _db.StateSnapshots.AsNoTracking()
                    .Where(s => s.GenerationRunId == prevRun.Id && s.Step == PipelineStep.PostState)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);
                prevEnd = snap?.StateJson;
            }
        }

        string? defP = null;
        string? defT = null;
        if (idx > 0)
        {
            for (var i = idx - 1; i >= 0; i--)
            {
                var ps = await _db.Scenes.AsNoTracking().FirstAsync(s => s.Id == orderedIds[i], cancellationToken);
                if (defP is null && !string.IsNullOrWhiteSpace(ps.NarrativePerspective))
                    defP = ps.NarrativePerspective;
                if (defT is null && !string.IsNullOrWhiteSpace(ps.NarrativeTense))
                    defT = ps.NarrativeTense;
                if (defP is not null && defT is not null)
                    break;
            }
        }

        return Ok(new SceneWorkflowContextDto
        {
            HasPreviousScene = hasPrev,
            PreviousSceneEndStateJson = prevEnd,
            DefaultNarrativePerspective = defP,
            DefaultNarrativeTense = defT
        });
    }

    [HttpPost("books/{bookId:guid}/scene-synopsis/suggest-world-elements")]
    public async Task<ActionResult<SuggestWorldElementsResponse>> SuggestWorldElements(
        Guid bookId,
        [FromBody] SuggestWorldElementsBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Synopsis))
            return BadRequest("synopsis is required.");
        var ids = await _worldBuilding.SuggestWorldElementsForSynopsisAsync(bookId, body.Synopsis, cancellationToken);
        return Ok(new SuggestWorldElementsResponse { ElementIds = ids });
    }

    [HttpPatch("chapters/{chapterId:guid}")]
    public async Task<ActionResult> PatchChapter(Guid chapterId, [FromBody] PatchChapterBody body,
        CancellationToken cancellationToken)
    {
        var ch = await _db.Chapters.FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (ch is null)
            return NotFound();
        if (body.IsComplete is { } ic)
            ch.IsComplete = ic;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public sealed class PatchSceneBody
    {
        public string? Title { get; set; }
        public string? Synopsis { get; set; }
        public string? Instructions { get; set; }
        public string? ExpectedEndStateNotes { get; set; }
        public string? NarrativePerspective { get; set; }
        public string? NarrativeTense { get; set; }
        public string? BeginningStateJson { get; set; }
    }

    public sealed class SceneWorkflowContextDto
    {
        public bool HasPreviousScene { get; set; }
        public string? PreviousSceneEndStateJson { get; set; }
        public string? DefaultNarrativePerspective { get; set; }
        public string? DefaultNarrativeTense { get; set; }
    }

    public sealed class SuggestWorldElementsBody
    {
        public string Synopsis { get; set; } = "";
    }

    public sealed class SuggestWorldElementsResponse
    {
        public IReadOnlyList<Guid> ElementIds { get; set; } = Array.Empty<Guid>();
    }

    public sealed class PatchChapterBody
    {
        public bool? IsComplete { get; set; }
    }
}
