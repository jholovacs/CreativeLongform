using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Tests;

public class GenerationOrchestratorTests
{
    [Fact]
    public async Task ConvertBeginningStateFromProseAsync_persists_normalized_json_from_model()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraAtKitchen);

        var result = await harness.Orchestrator.ConvertBeginningStateFromProseAsync(
            scene.Id,
            "Mara is in the kitchen at scene open.");

        Assert.Contains("\"Mara\"", result.BeginningStateJson, StringComparison.Ordinal);
        var updated = await WorkflowTestData.ReloadSceneAsync(harness.Db, scene.Id);
        Assert.Equal("Mara is in the kitchen at scene open.", updated.BeginningStateProse);
        Assert.Contains("\"kitchen\"", updated.BeginningStateJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConvertBeginningStateFromProseAsync_falls_back_to_prior_scene_end_state_when_model_empty()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, chapter, scene1) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        var scene2 = await WorkflowTestData.AddSecondSceneAsync(harness.Db, chapter);
        scene1.ApprovedStateTableJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        harness.Ollama.EnqueueEmptyJson(8);

        var result = await harness.Orchestrator.ConvertBeginningStateFromProseAsync(
            scene2.Id,
            "Author prose that the model fails to convert.");

        Assert.Contains("\"Mara\"", result.BeginningStateJson, StringComparison.Ordinal);
        Assert.Contains("\"kitchen\"", result.BeginningStateJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeriveBeginningStateAsync_first_scene_uses_pre_state_model()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraAtKitchen);

        var result = await harness.Orchestrator.DeriveBeginningStateAsync(scene.Id);

        Assert.False(result.DerivedFromPreviousScene);
        Assert.Contains("\"kitchen\"", result.BeginningStateJson, StringComparison.Ordinal);
        Assert.Contains("test-pre", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeriveBeginningStateAsync_with_previous_scene_uses_post_state_model_on_handoff()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, chapter, scene1) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        var scene2 = await WorkflowTestData.AddSecondSceneAsync(harness.Db, chapter);

        scene1.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        scene1.ManuscriptText = "Mara walked into the hallway, letter in hand.";
        await harness.Db.SaveChangesAsync();

        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var result = await harness.Orchestrator.DeriveBeginningStateAsync(scene2.Id);

        Assert.True(result.DerivedFromPreviousScene);
        Assert.Contains("\"hallway\"", result.BeginningStateJson, StringComparison.Ordinal);
        Assert.Contains("test-post", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeriveBeginningStateAsync_previous_scene_without_prose_throws()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, chapter, scene1) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        var scene2 = await WorkflowTestData.AddSecondSceneAsync(harness.Db, chapter);
        scene1.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Orchestrator.DeriveBeginningStateAsync(scene2.Id));

        Assert.Contains("Previous scene has no manuscript", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizeGenerationAsync_with_approved_state_skips_post_state_llm()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        var run = await WorkflowTestData.SeedAwaitingReviewRunAsync(
            harness.Db, scene, "Mara crossed the kitchen.", NarrativeStateTestFixtures.MaraAtKitchen);

        harness.Ollama.Enqueue("""{"pass":true,"gaps":[]}""");

        var result = await harness.Orchestrator.FinalizeGenerationAsync(
            scene.Id, run.Id, acceptedDraftText: null, approvedStateTableJson: NarrativeStateTestFixtures.MaraInHallway);

        Assert.Contains("\"hallway\"", result.StateTableJson, StringComparison.Ordinal);
        var updated = await WorkflowTestData.ReloadSceneAsync(harness.Db, scene.Id);
        Assert.Equal("Mara crossed the kitchen.", updated.ManuscriptText);
        Assert.Equal(result.StateTableJson, updated.ApprovedStateTableJson);
        Assert.Single(harness.Ollama.Calls);
    }

    [Fact]
    public async Task FinalizeGenerationAsync_uses_pending_post_state_when_model_returns_empty()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        scene.PendingPostStateJson = NarrativeStateTestFixtures.MaraInHallway;
        await harness.Db.SaveChangesAsync();

        var run = await WorkflowTestData.SeedAwaitingReviewRunAsync(
            harness.Db, scene, "Mara left the kitchen.", NarrativeStateTestFixtures.MaraAtKitchen);

        harness.Ollama.EnqueueEmptyJson(4);
        harness.Ollama.Enqueue("""{"pass":true,"gaps":[]}""");

        var result = await harness.Orchestrator.FinalizeGenerationAsync(
            scene.Id, run.Id, acceptedDraftText: null, approvedStateTableJson: null);

        Assert.Contains("\"hallway\"", result.StateTableJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizeGenerationAsync_falls_back_to_beginning_state_when_no_preview()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        scene.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        var run = await WorkflowTestData.SeedAwaitingReviewRunAsync(
            harness.Db, scene, "Short draft.", NarrativeStateTestFixtures.MaraAtKitchen);

        harness.Ollama.EnqueueEmptyJson(4);
        harness.Ollama.Enqueue("""{"pass":true,"gaps":[]}""");

        var result = await harness.Orchestrator.FinalizeGenerationAsync(
            scene.Id, run.Id, acceptedDraftText: null, approvedStateTableJson: null);

        Assert.Contains("\"kitchen\"", result.StateTableJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FinalizeGenerationAsync_copies_end_state_to_existing_next_scene()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, chapter, scene1) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        var scene2 = await WorkflowTestData.AddSecondSceneAsync(harness.Db, chapter);
        scene1.BeginningStateJson = NarrativeStateTestFixtures.MaraAtKitchen;
        await harness.Db.SaveChangesAsync();

        var run = await WorkflowTestData.SeedAwaitingReviewRunAsync(
            harness.Db, scene1, "Mara left.", NarrativeStateTestFixtures.MaraAtKitchen);

        harness.Ollama.Enqueue("""{"pass":true,"gaps":[]}""");

        var result = await harness.Orchestrator.FinalizeGenerationAsync(
            scene1.Id, run.Id, acceptedDraftText: null, approvedStateTableJson: NarrativeStateTestFixtures.MaraInHallway);

        Assert.Equal(scene2.Id, result.NextSceneId);
        var next = await WorkflowTestData.ReloadSceneAsync(harness.Db, scene2.Id);
        Assert.Contains("\"hallway\"", next.BeginningStateJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CorrectDraftAsync_revises_draft_and_recomputes_post_state()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        const string draft = "Mara stood in the kitchen.";
        var run = await WorkflowTestData.SeedAwaitingReviewRunAsync(
            harness.Db, scene, draft, NarrativeStateTestFixtures.MaraAtKitchen);

        harness.Ollama.Enqueue("""
            {"action":"propose_patch","paragraphStart":0,"paragraphEnd":0,"replacement":"Mara paced the kitchen, restless.","reason":"Make her restless while pacing."}
            """);
        harness.Ollama.Enqueue("""{"action":"run_compliance_check"}""");
        harness.Ollama.Enqueue("""{"pass":true,"violations":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue("""{"action":"run_quality_check"}""");
        harness.Ollama.Enqueue("""{"score":82,"issues":[],"fixInstructions":[]}""");
        harness.Ollama.Enqueue("""{"action":"finish","reason":"Author mission complete; checks passed."}""");
        harness.Ollama.Enqueue(NarrativeStateTestFixtures.MaraInHallway);

        var result = await harness.Orchestrator.CorrectDraftAsync(
            scene.Id, run.Id, "Make her restless while pacing.");

        Assert.Contains("restless", result.CorrectedDraftText, StringComparison.Ordinal);
        Assert.Contains("\"hallway\"", result.PendingPostStateJson!, StringComparison.Ordinal);
    }
}
