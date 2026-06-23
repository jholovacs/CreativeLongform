using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using CreativeLongform.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Narrative;

/// <summary>Story-order scene lookup and end-state continuity for workflow and generation.</summary>
public static class SceneContinuityResolver
{
    public static async Task<List<Guid>> GetOrderedSceneIdsInBookAsync(
        ICreativeLongformDbContext db,
        Guid bookId,
        CancellationToken cancellationToken = default)
    {
        return await db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .Where(s => s.Chapter.BookId == bookId)
            .OrderBy(s => s.Chapter.Order).ThenBy(s => s.Order)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public static async Task<Guid?> GetPreviousSceneIdInBookAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .FirstAsync(s => s.Id == sceneId, cancellationToken);
        var orderedIds = await GetOrderedSceneIdsInBookAsync(db, scene.Chapter.BookId, cancellationToken);
        var idx = orderedIds.IndexOf(sceneId);
        return idx > 0 ? orderedIds[idx - 1] : null;
    }

    /// <summary>Approved end-state table, else post-state snapshot from the last succeeded run.</summary>
    public static async Task<string?> GetSceneEndStateJsonAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var scene = await db.Scenes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            return null;
        if (!string.IsNullOrWhiteSpace(scene.ApprovedStateTableJson))
            return scene.ApprovedStateTableJson;

        var prevRun = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == sceneId && r.Status == GenerationRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (prevRun is null)
            return null;

        var snap = await db.StateSnapshots
            .AsNoTracking()
            .Where(s => s.GenerationRunId == prevRun.Id && s.Step == PipelineStep.PostState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return snap?.StateJson;
    }

    public static async Task<string?> GetPreviousSceneEndStateJsonAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        var prevId = await GetPreviousSceneIdInBookAsync(db, sceneId, cancellationToken);
        if (prevId is null)
            return null;
        return await GetSceneEndStateJsonAsync(db, prevId.Value, cancellationToken);
    }
}
