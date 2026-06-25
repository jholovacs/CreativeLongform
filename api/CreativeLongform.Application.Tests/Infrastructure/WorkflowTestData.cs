using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using CreativeLongform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Application.Tests.Infrastructure;

internal static class WorkflowTestData
{
    public static async Task<Book> SeedBookAsync(CreativeLongformDbContext db, string title = "Test Novel")
    {
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = title,
            StoryToneAndStyle = "Literary fiction",
            Synopsis = "A woman discovers a letter.",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        return book;
    }

    public static async Task<WorldElement> SeedWorldElementAsync(
        CreativeLongformDbContext db,
        Guid bookId,
        string title,
        WorldElementKind kind = WorldElementKind.Geography,
        string summary = "Summary",
        WorldElementStatus status = WorldElementStatus.Canon)
    {
        var now = DateTimeOffset.UtcNow;
        var element = new WorldElement
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            Kind = kind,
            Title = title,
            Summary = summary,
            Detail = string.Empty,
            Status = status,
            Provenance = WorldElementProvenance.Manual,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.WorldElements.Add(element);
        await db.SaveChangesAsync();
        return element;
    }

    public static async Task<WorldElementLink> SeedWorldElementLinkAsync(
        CreativeLongformDbContext db,
        Guid fromId,
        Guid toId,
        string relationLabel)
    {
        var link = new WorldElementLink
        {
            Id = Guid.NewGuid(),
            FromWorldElementId = fromId,
            ToWorldElementId = toId,
            RelationLabel = relationLabel
        };
        db.WorldElementLinks.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    public static async Task<TimelineEntry> SeedTimelineEntryAsync(
        CreativeLongformDbContext db,
        Guid bookId,
        string title,
        Guid? worldElementId = null)
    {
        var entry = new TimelineEntry
        {
            Id = Guid.NewGuid(),
            BookId = bookId,
            Kind = TimelineEntryKind.WorldEvent,
            SortKey = 1,
            Title = title,
            Summary = "A story beat.",
            WorldElementId = worldElementId
        };
        db.TimelineEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    public static async Task<(Book Book, Chapter Chapter, Scene Scene)> SeedBookWithSceneAsync(
        CreativeLongformDbContext db,
        string sceneTitle = "Opening",
        string synopsis = "Mara enters the kitchen.")
    {
        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var book = new Book
        {
            Id = bookId,
            Title = "Test Novel",
            StoryToneAndStyle = "Literary fiction",
            Synopsis = "A woman discovers a letter.",
            CreatedAt = now
        };
        var chapter = new Chapter
        {
            Id = chapterId,
            BookId = bookId,
            Order = 1,
            Title = "Chapter One"
        };
        var scene = new Scene
        {
            Id = sceneId,
            ChapterId = chapterId,
            Order = 1,
            Title = sceneTitle,
            Synopsis = synopsis,
            Instructions = "Keep the tone intimate.",
            NarrativePerspective = "third limited",
            NarrativeTense = "past"
        };

        db.Books.Add(book);
        db.Chapters.Add(chapter);
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return (book, chapter, scene);
    }

    public static async Task<Scene> AddSecondSceneAsync(
        CreativeLongformDbContext db,
        Chapter chapter,
        int order = 2,
        string title = "Scene Two")
    {
        var scene = new Scene
        {
            Id = Guid.NewGuid(),
            ChapterId = chapter.Id,
            Order = order,
            Title = title,
            Synopsis = "Mara reads the letter.",
            Instructions = string.Empty,
            NarrativePerspective = "third limited",
            NarrativeTense = "past"
        };
        db.Scenes.Add(scene);
        await db.SaveChangesAsync();
        return scene;
    }

    public static async Task<GenerationRun> SeedAwaitingReviewRunAsync(
        CreativeLongformDbContext db,
        Scene scene,
        string draftText,
        string? preStateJson = null)
    {
        var runId = Guid.NewGuid();
        var run = new GenerationRun
        {
            Id = runId,
            SceneId = scene.Id,
            Status = GenerationRunStatus.AwaitingUserReview,
            StartedAt = DateTimeOffset.UtcNow,
            FinalDraftText = draftText,
            StopAfterDraft = true,
            SkipQualityGate = true
        };
        db.GenerationRuns.Add(run);

        if (!string.IsNullOrWhiteSpace(preStateJson))
        {
            db.StateSnapshots.Add(new StateSnapshot
            {
                Id = Guid.NewGuid(),
                GenerationRunId = runId,
                Step = PipelineStep.PreState,
                SchemaVersion = 1,
                StateJson = preStateJson,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync();
        return run;
    }

    public static async Task<Scene> ReloadSceneAsync(CreativeLongformDbContext db, Guid sceneId)
        => await db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .FirstAsync(s => s.Id == sceneId);
}
