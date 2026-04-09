using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Infrastructure.Data;

public static class DbSeeder
{
    public static async Task SeedDevelopmentAsync(CreativeLongformDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Books.AnyAsync(cancellationToken))
            return;

        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var weCafeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        db.Books.Add(new Book
        {
            Id = bookId,
            Title = "Sample novel",
            StoryToneAndStyle = "Literary realism with quiet tension",
            ContentStyleNotes = "Grounded dialogue; avoid melodrama.",
            CreatedAt = now
        });

        db.Chapters.Add(new Chapter
        {
            Id = chapterId,
            BookId = bookId,
            Order = 1,
            Title = "Chapter 1"
        });

        db.Scenes.Add(new Scene
        {
            Id = sceneId,
            ChapterId = chapterId,
            Order = 1,
            Title = "Opening scene",
            Synopsis = string.Empty,
            Instructions =
                "Two characters meet in a quiet café. Establish tension through dialogue and small gestures. End on an unresolved question.",
            ExpectedEndStateNotes = "Both remain seated; neither has agreed to the proposal."
        });

        db.WorldElements.Add(new WorldElement
        {
            Id = weCafeId,
            BookId = bookId,
            Kind = WorldElementKind.Geography,
            Title = "The corner café",
            Slug = "corner-cafe",
            Summary = "Small neighborhood café with a few tables; rain on the windows in the evenings.",
            Detail = "The café is a recurring meeting point; staff know regulars by drink order.",
            Status = WorldElementStatus.Canon,
            Provenance = WorldElementProvenance.Manual,
            CreatedAt = now,
            UpdatedAt = now
        });

        db.SceneWorldElements.Add(new SceneWorldElement
        {
            SceneId = sceneId,
            WorldElementId = weCafeId
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
