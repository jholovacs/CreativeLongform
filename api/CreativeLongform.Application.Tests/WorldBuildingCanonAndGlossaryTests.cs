using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Tests.Infrastructure;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Tests;

public class WorldBuildingCanonAndGlossaryTests
{
    [Fact]
    public async Task ReviewLinksCanonAsync_parses_add_link_proposal()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character);
        await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography);

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.CanonReviewAddLinkJson("Mara", "Harbor Town", "Works in"));

        var result = await harness.WorldBuilding.ReviewLinksCanonAsync(book.Id, mara.Id);

        var proposal = Assert.Single(result.Proposals);
        Assert.Equal("add_link", proposal.Kind);
        Assert.Equal("Works in", proposal.RelationLabel);
        Assert.Equal(mara.Id, proposal.FromWorldElementId);
    }

    [Fact]
    public async Task ReviewLinksCanonAsync_parses_remove_and_change_relation()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character);
        var town = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography);
        var link = await WorkflowTestData.SeedWorldElementLinkAsync(harness.Db, mara.Id, town.Id, "Old label");

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.CanonReviewRemoveLinkJson(link.Id));

        var removeResult = await harness.WorldBuilding.ReviewLinksCanonAsync(book.Id, mara.Id);
        var remove = Assert.Single(removeResult.Proposals);
        Assert.Equal("remove_link", remove.Kind);
        Assert.Equal(link.Id, remove.LinkId);

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.CanonReviewChangeRelationJson(link.Id, "Lives in"));

        var changeResult = await harness.WorldBuilding.ReviewLinksCanonAsync(book.Id, mara.Id);
        var change = Assert.Single(changeResult.Proposals);
        Assert.Equal("change_relation", change.Kind);
        Assert.Equal("Lives in", change.NewRelationLabel);
    }

    [Fact]
    public async Task ReviewLinksCanonAsync_returns_empty_when_focus_missing()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);

        var result = await harness.WorldBuilding.ReviewLinksCanonAsync(book.Id, Guid.NewGuid());

        Assert.Empty(result.Proposals);
        Assert.Empty(harness.Ollama.Calls);
    }

    [Fact]
    public async Task ApplyLinkCanonReviewAsync_adds_removes_and_updates_links()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character, status: WorldElementStatus.Draft);
        var town = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Harbor Town", WorldElementKind.Geography, status: WorldElementStatus.Draft);
        var dock = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Old Dock", WorldElementKind.Geography, status: WorldElementStatus.Draft);
        var stale = await WorkflowTestData.SeedWorldElementLinkAsync(harness.Db, mara.Id, dock.Id, "Knows");

        var result = await harness.WorldBuilding.ApplyLinkCanonReviewAsync(book.Id,
        [
            new ApplyLinkCanonItem { Kind = "remove_link", LinkId = stale.Id },
            new ApplyLinkCanonItem
            {
                Kind = "add_link",
                FromWorldElementId = mara.Id,
                ToWorldElementId = town.Id,
                RelationLabel = "lives_in"
            }
        ]);

        Assert.Equal(1, result.LinksRemoved);
        Assert.Equal(1, result.LinksAdded);
        var links = await harness.Db.WorldElementLinks.AsNoTracking().ToListAsync();
        var live = Assert.Single(links);
        Assert.Equal("Lives In", live.RelationLabel);

        var promoted = await harness.Db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == book.Id && w.Status == WorldElementStatus.Canon)
            .Select(w => w.Id)
            .ToListAsync();
        Assert.Contains(mara.Id, promoted);
        Assert.Contains(town.Id, promoted);
    }

    [Fact]
    public async Task ApplyLinkCanonReviewAsync_updates_timeline_world_element_link()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara", WorldElementKind.Character);
        var entry = await WorkflowTestData.SeedTimelineEntryAsync(harness.Db, book.Id, "Harbor riot");

        var result = await harness.WorldBuilding.ApplyLinkCanonReviewAsync(book.Id,
        [
            new ApplyLinkCanonItem
            {
                Kind = "set_timeline_link",
                TimelineEntryId = entry.Id,
                WorldElementId = mara.Id
            }
        ]);

        Assert.Equal(1, result.TimelineEntriesUpdated);
        var updated = await harness.Db.TimelineEntries.AsNoTracking().FirstAsync(t => t.Id == entry.Id);
        Assert.Equal(mara.Id, updated.WorldElementId);
    }

    [Fact]
    public async Task BuildGlossaryMarkdownAsync_without_llm_includes_primary_entries()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara Vale", WorldElementKind.Character, "A clerk");

        var md = await harness.WorldBuilding.BuildGlossaryMarkdownAsync(book.Id, useLlmForAlternateNames: false);

        Assert.NotNull(md);
        Assert.Contains("# Glossary", md!, StringComparison.Ordinal);
        Assert.Contains("Mara Vale", md!, StringComparison.Ordinal);
        Assert.Contains("**Kind:** Character", md!, StringComparison.Ordinal);
        Assert.Empty(harness.Ollama.Calls);
    }

    [Fact]
    public async Task BuildGlossaryMarkdownAsync_with_llm_adds_alternate_name_stubs()
    {
        await using var harness = OrchestratorTestHarness.Create();
        var book = await WorkflowTestData.SeedBookAsync(harness.Db);
        var mara = await WorkflowTestData.SeedWorldElementAsync(
            harness.Db, book.Id, "Mara Vale", WorldElementKind.Character, "A clerk");

        harness.Ollama.Enqueue(WorldBuildingTestFixtures.GlossaryAlternatesJson(mara.Id, "Red"));

        var md = await harness.WorldBuilding.BuildGlossaryMarkdownAsync(book.Id, useLlmForAlternateNames: true);

        Assert.NotNull(md);
        Assert.Contains("Red", md!, StringComparison.Ordinal);
        Assert.Contains("See:", md!, StringComparison.Ordinal);
        var llmCall = await harness.Db.LlmCalls.AsNoTracking().SingleAsync();
        Assert.Equal(PipelineStep.WorldBuildingGlossary, llmCall.Step);
    }
}
