using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/books")]
public sealed class BooksCreateController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public BooksCreateController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<CreatedBookResponse>> Create([FromBody] CreateBookBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest("Title is required.");

        var bookId = Guid.NewGuid();
        var chapterId = Guid.NewGuid();
        var sceneId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        _db.Books.Add(new Book
        {
            Id = bookId,
            Title = body.Title.Trim(),
            StoryToneAndStyle = body.StoryToneAndStyle?.Trim() ?? string.Empty,
            ContentStyleNotes = string.IsNullOrWhiteSpace(body.ContentStyleNotes) ? null : body.ContentStyleNotes.Trim(),
            Synopsis = string.IsNullOrWhiteSpace(body.Synopsis) ? null : body.Synopsis.Trim(),
            CreatedAt = now
        });

        _db.Chapters.Add(new Chapter
        {
            Id = chapterId,
            BookId = bookId,
            Order = 1,
            Title = "Chapter 1"
        });

        _db.Scenes.Add(new Scene
        {
            Id = sceneId,
            ChapterId = chapterId,
            Order = 1,
            Title = "Opening scene",
            Synopsis = string.Empty,
            Instructions =
                "Establish setting and characters. Revise this instruction in the scene workflow when you are ready to draft.",
            ExpectedEndStateNotes = null
        });

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new CreatedBookResponse { Id = bookId, Title = body.Title.Trim() });
    }

    public sealed class CreateBookBody
    {
        public string? Title { get; set; }
        public string? StoryToneAndStyle { get; set; }
        public string? ContentStyleNotes { get; set; }
        public string? Synopsis { get; set; }
    }

    public sealed class CreatedBookResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
    }
}
