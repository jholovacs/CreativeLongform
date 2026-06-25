using CreativeLongform.Application.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Tests;

public class DraftRecommendationServiceTests
{
    [Fact]
    public async Task GetRecommendationsAsync_parses_replace_and_rewrite_items()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        harness.Ollama.Enqueue(DraftTestFixtures.RecommendationsJson);

        var result = await harness.DraftRecommendations.GetRecommendationsAsync(scene.Id, DraftTestFixtures.SceneDraft);

        Assert.Equal(2, result.Items.Count);
        var replace = Assert.Single(result.Items, i => i.Kind == "replace");
        Assert.Equal(0, replace.ParagraphStart);
        Assert.Contains("Rain needled", replace.ReplacementText!, StringComparison.Ordinal);
        var rewrite = Assert.Single(result.Items, i => i.Kind == "rewrite");
        Assert.Contains("Shorten", rewrite.RewriteInstruction!, StringComparison.Ordinal);
        Assert.Contains("test-critic", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);

        var llmCall = await harness.Db.LlmCalls.AsNoTracking().SingleAsync();
        Assert.Equal(Domain.Enums.PipelineStep.DraftRecommendationAnalysis, llmCall.Step);
    }

    [Fact]
    public async Task GetRecommendationsAsync_skips_items_with_missing_payload()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        harness.Ollama.Enqueue("""
            {
              "items": [
                { "kind": "replace", "paragraphStart": 0, "paragraphEnd": 0, "problem": "x", "replacementText": "" },
                { "kind": "rewrite", "paragraphStart": 0, "paragraphEnd": 0, "problem": "y", "rewriteInstruction": "Fix tense" }
              ]
            }
            """);

        var result = await harness.DraftRecommendations.GetRecommendationsAsync(scene.Id, DraftTestFixtures.SceneDraft);

        var item = Assert.Single(result.Items);
        Assert.Equal("rewrite", item.Kind);
    }

    [Fact]
    public async Task GetRecommendationsAsync_throws_when_draft_empty()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => harness.DraftRecommendations.GetRecommendationsAsync(scene.Id, "   "));
    }

    [Fact]
    public async Task GetRecommendationsAsync_throws_when_model_returns_invalid_json()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var (_, _, scene) = await WorkflowTestData.SeedBookWithSceneAsync(harness.Db);
        harness.Ollama.Enqueue("not json");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.DraftRecommendations.GetRecommendationsAsync(scene.Id, DraftTestFixtures.SceneDraft));
    }
}
