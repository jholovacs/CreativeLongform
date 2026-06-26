using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Tests;

public class GenerationOrchestratorPipelineTests
{
    [Fact]
    public async Task StartGenerationAsync_stop_after_draft_uses_existing_beginning_state()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.AwaitingUserReview, run.Status);
        Assert.Contains(DraftTestFixtures.SceneDraft.Trim()[..40], run.FinalDraftText!, StringComparison.Ordinal);

        Assert.Equal(3, harness.Ollama.Calls.Count);
        Assert.False(harness.Ollama.Calls[0].JsonFormat);
        Assert.Contains("test-writer", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);
        Assert.True(harness.Ollama.Calls[1].JsonFormat);
        Assert.Contains("test-critic", harness.Ollama.Calls[1].Model, StringComparison.Ordinal);
        Assert.Contains("test-post", harness.Ollama.Calls[2].Model, StringComparison.Ordinal);

        var snapshots = await harness.Db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == runId)
            .Select(s => s.Step)
            .ToListAsync();
        Assert.Contains(PipelineStep.PreState, snapshots);
        Assert.Contains(PipelineStep.PostState, snapshots);

        var updated = await WorkflowTestData.ReloadSceneAsync(harness.Db, scene.Id);
        Assert.Contains("\"hallway\"", updated.PendingPostStateJson!, StringComparison.Ordinal);
        Assert.Contains("kitchen window", updated.LatestDraftText!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StartGenerationAsync_infers_pre_state_when_beginning_state_missing()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);

        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraAtKitchen);
        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.AwaitingUserReview, run.Status);
        Assert.Contains("test-pre", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartGenerationAsync_idempotency_key_returns_same_pending_run()
    {
        await using var harness = OrchestratorTestHarness.Create(o => o.DraftExpandIfShort = false);
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        const string key = "test-idempotency";
        var first = await harness.Orchestrator.StartGenerationAsync(scene.Id, key, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });
        var second = await harness.Orchestrator.StartGenerationAsync(scene.Id, key, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        Assert.Equal(first, second);
        await harness.WaitForRunAsync(first);
        var count = await harness.Db.GenerationRuns.AsNoTracking().CountAsync(r => r.SceneId == scene.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CancelGenerationAsync_marks_running_run_cancelled()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.PauseBeforeNextChat();
        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        GenerationRunStatus status = GenerationRunStatus.Pending;
        for (var i = 0; i < 100; i++)
        {
            await Task.Delay(25);
            status = await harness.GetRunStatusAsync(runId);
            if (status == GenerationRunStatus.Running)
                break;
        }

        Assert.Equal(GenerationRunStatus.Running, status);

        var cancelled = await harness.Orchestrator.CancelGenerationAsync(scene.Id, runId);
        harness.Ollama.ReleasePause();

        Assert.True(cancelled);
        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.Cancelled, run.Status);
    }

    [Fact]
    public async Task StartGenerationAsync_runs_quality_gate_when_enabled()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
            o.QualityGateEnabled = true;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue("""{"score":42,"issues":["flat opening"],"fixInstructions":["Add sensory detail"]}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = true,
            MinWordsOverride = 50
        });

        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.AwaitingUserReview, run.Status);
        Assert.Equal(4, harness.Ollama.Calls.Count);
        Assert.Contains("test-critic", harness.Ollama.Calls[2].Model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartGenerationAsync_records_compliance_failure_but_completes_review()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue("""
            {"pass":false,"violations":["Invented kinship — draft quotes 'Her sister had written' with no brief support"],"fixInstructions":["Revise 'Her sister had written from the capital' to an authorized relationship"]}
            """);
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = true,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.AwaitingUserReview, run.Status);

        var compliance = await harness.Db.ComplianceEvaluations.AsNoTracking()
            .SingleAsync(c => c.GenerationRunId == runId);
        Assert.False(compliance.Passed);
    }

    [Fact]
    public async Task StartGenerationAsync_full_pipeline_succeeds_without_stop_after_draft()
    {
        await using var harness = OrchestratorTestHarness.Create(o =>
        {
            o.DraftMinWords = 50;
            o.DraftExpandIfShort = false;
        });
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(DraftTestFixtures.SceneDraft);
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);
        harness.Ollama.Enqueue("""{"pass":true,"gaps":[]}""");
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");

        var runId = await harness.Orchestrator.StartGenerationAsync(scene.Id, null, new GenerationStartOptions
        {
            StopAfterDraft = false,
            SkipQualityGate = true,
            MinWordsOverride = 50
        });

        var run = await harness.WaitForRunAsync(runId);
        Assert.Equal(GenerationRunStatus.Succeeded, run.Status);

        var updated = await WorkflowTestData.ReloadSceneAsync(harness.Db, scene.Id);
        Assert.Null(updated.PendingPostStateJson);
        Assert.Contains("kitchen window", updated.LatestDraftText!, StringComparison.OrdinalIgnoreCase);
    }
}
