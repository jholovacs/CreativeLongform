using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/books/{bookId:guid}")]
public sealed class BookStoryController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public BookStoryController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPatch("story-profile")]
    public async Task<ActionResult> PatchStoryProfile(
        Guid bookId,
        [FromBody] StoryProfileBody body,
        CancellationToken cancellationToken)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null)
            return NotFound();

        if (body.StoryToneAndStyle is not null)
            book.StoryToneAndStyle = body.StoryToneAndStyle;
        if (body.ContentStyleNotes is not null)
            book.ContentStyleNotes = body.ContentStyleNotes;
        if (body.MeasurementPreset is not null)
            book.MeasurementPreset = body.MeasurementPreset.Value;
        if (body.MeasurementSystemJson is not null)
            book.MeasurementSystemJson = body.MeasurementSystemJson;
        if (body.Synopsis is not null)
            book.Synopsis = body.Synopsis;

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public sealed class StoryProfileBody
    {
        public string? StoryToneAndStyle { get; set; }
        public string? ContentStyleNotes { get; set; }
        public MeasurementPreset? MeasurementPreset { get; set; }
        public string? MeasurementSystemJson { get; set; }
        public string? Synopsis { get; set; }
    }
}
