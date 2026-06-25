using CreativeLongform.Application.Narrative;
using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Enums;
using CreativeLongform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeLongform.Application.Tests;

public class SceneContinuityResolverTests
{
    private static CreativeLongformDbContext CreateDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CreativeLongformDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        return services.BuildServiceProvider().GetRequiredService<CreativeLongformDbContext>();
    }

    [Fact]
    public async Task GetPreviousSceneIdInBookAsync_returns_prior_scene_in_story_order()
    {
        await using var db = CreateDb();
        var (_, chapter, scene1) = await WorkflowTestData.SeedBookWithSceneAsync(db);
        var scene2 = await WorkflowTestData.AddSecondSceneAsync(db, chapter);

        var prev = await SceneContinuityResolver.GetPreviousSceneIdInBookAsync(db, scene2.Id);

        Assert.Equal(scene1.Id, prev);
    }

    [Fact]
    public async Task GetPreviousSceneIdInBookAsync_returns_null_for_first_scene()
    {
        await using var db = CreateDb();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(db);

        var prev = await SceneContinuityResolver.GetPreviousSceneIdInBookAsync(db, scene.Id);

        Assert.Null(prev);
    }

    [Fact]
    public async Task GetSceneEndStateJsonAsync_prefers_approved_state_table()
    {
        await using var db = CreateDb();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(db);
        scene.ApprovedStateTableJson = NarrativeStateTestFixtures.MaraInHallway;
        await db.SaveChangesAsync();

        var json = await SceneContinuityResolver.GetSceneEndStateJsonAsync(db, scene.Id);

        Assert.Contains("\"hallway\"", json!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSceneEndStateJsonAsync_falls_back_to_post_state_snapshot()
    {
        await using var db = CreateDb();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(db);
        var runId = Guid.NewGuid();
        db.GenerationRuns.Add(new Domain.Entities.GenerationRun
        {
            Id = runId,
            SceneId = scene.Id,
            Status = GenerationRunStatus.Succeeded,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        db.StateSnapshots.Add(new Domain.Entities.StateSnapshot
        {
            Id = Guid.NewGuid(),
            GenerationRunId = runId,
            Step = PipelineStep.PostState,
            SchemaVersion = 1,
            StateJson = NarrativeStateTestFixtures.MaraAtKitchen,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var json = await SceneContinuityResolver.GetSceneEndStateJsonAsync(db, scene.Id);

        Assert.Contains("\"kitchen\"", json!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPreviousSceneEndStateJsonAsync_returns_null_when_no_prior_scene()
    {
        await using var db = CreateDb();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(db);

        var json = await SceneContinuityResolver.GetPreviousSceneEndStateJsonAsync(db, scene.Id);

        Assert.Null(json);
    }
}
