using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Tests;

public class WorldBuildingServiceTests
{
    [Fact]
    public async Task GenerateFromPromptAsync_creates_draft_elements_and_suggested_links()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        harness.Ollama.Enqueue(WorldBuildingTestFixtures.HarborBatchJson);

        var result = await harness.WorldBuilding.GenerateFromPromptAsync(book.Id, "A harbor town story with Mara.");

        Assert.Equal(2, result.CreatedElementIds.Count);
        var link = Assert.Single(result.SuggestedLinks);
        Assert.Equal("Lives in", link.RelationLabel);
        Assert.Equal("Mara", link.FromTitle);
        Assert.Equal("Harbor Town", link.ToTitle);

        var elements = await harness.Db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == book.Id)
            .ToListAsync();
        Assert.Equal(2, elements.Count);
        Assert.All(elements, e => Assert.Equal(WorldElementStatus.Draft, e.Status));
        Assert.All(elements, e => Assert.Equal(WorldElementProvenance.LlmGenerated, e.Provenance));
        Assert.Contains("test-world", harness.Ollama.Calls[0].Model, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExtractFromTextAsync_marks_elements_as_llm_extracted()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        harness.Ollama.Enqueue(WorldBuildingTestFixtures.HarborBatchJson);

        var result = await harness.WorldBuilding.ExtractFromTextAsync(book.Id, "Mara lives in Harbor Town.");

        Assert.Equal(2, result.CreatedElementIds.Count);
        var element = await harness.Db.WorldElements.AsNoTracking()
            .FirstAsync(w => w.Title == "Harbor Town");
        Assert.Equal(WorldElementProvenance.LlmExtracted, element.Provenance);
    }

    [Fact]
    public async Task BootstrapStoryAsync_updates_book_metadata_and_generates_world()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        harness.Ollama.Enqueue(WorldBuildingTestFixtures.HarborBatchJson);

        var result = await harness.WorldBuilding.BootstrapStoryAsync(book.Id, new StoryBootstrapRequest
        {
            StoryToneAndStyle = "Gothic mystery",
            Synopsis = "Secrets in a harbor town.",
            SourceText = "Fog rolls in each evening."
        });

        Assert.Equal(2, result.CreatedElementIds.Count);
        var updatedBook = await harness.Db.Books.AsNoTracking().FirstAsync(b => b.Id == book.Id);
        Assert.Equal("Gothic mystery", updatedBook.StoryToneAndStyle);
        Assert.Equal("Secrets in a harbor town.", updatedBook.Synopsis);
    }

    [Fact]
    public async Task SuggestLinksForElementAsync_resolves_titles_to_ids()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character, "Clerk");
        await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography, "Port city");

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.LinkSuggestJson("Mara", "Harbor Town", "Works in"));

        var links = await harness.WorldBuilding.SuggestLinksForElementAsync(book.Id, mara.Id);

        var link = Assert.Single(links);
        Assert.Equal(mara.Id, link.FromWorldElementId);
        Assert.Equal("Works in", link.RelationLabel);
    }

    [Fact]
    public async Task SuggestLinksForElementAsync_returns_empty_when_focus_missing()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);

        var links = await harness.WorldBuilding.SuggestLinksForElementAsync(book.Id, Guid.NewGuid());

        Assert.Empty(links);
        Assert.Empty(harness.Ollama.Calls);
    }

    [Fact]
    public async Task SuggestWorldElementsForSynopsisAsync_returns_only_valid_book_element_ids()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character);
        await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography);

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.SynopsisPickJson(mara.Id, Guid.NewGuid()));

        var ids = await harness.WorldBuilding.SuggestWorldElementsForSynopsisAsync(
            book.Id, "Mara returns to the harbor.");

        Assert.Equal([mara.Id], ids);
    }

    [Fact]
    public async Task ApplySuggestedLinksAsync_creates_link_rows()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character);
        var town = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography);

        var created = await harness.WorldBuilding.ApplySuggestedLinksAsync(book.Id,
        [
            new ApplySuggestedLinkItem
            {
                FromWorldElementId = mara.Id,
                ToWorldElementId = town.Id,
                RelationLabel = "lives_in"
            }
        ]);

        Assert.Equal(1, created);
        var link = await harness.Db.WorldElementLinks.AsNoTracking().SingleAsync();
        Assert.Equal("Lives In", link.RelationLabel);
    }
}
