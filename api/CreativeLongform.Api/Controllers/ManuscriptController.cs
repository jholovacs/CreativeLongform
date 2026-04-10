using CreativeLongform.Application.Manuscript;
using CreativeLongform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class ManuscriptController : ControllerBase
{
    private readonly CreativeLongformDbContext _db;

    public ManuscriptController(CreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpGet("books/{bookId:guid}/manuscript")]
    public async Task<ActionResult<ManuscriptResponseDto>> GetBookManuscript(Guid bookId, CancellationToken cancellationToken)
    {
        var book = await _db.Books.AsNoTracking()
            .Include(b => b.Chapters)
            .ThenInclude(c => c.Scenes)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null)
            return NotFound();

        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var computed = ManuscriptAssembly.AssembleBook(chapters);
        return Ok(ToDto(book.ManuscriptText, computed));
    }

    [HttpGet("chapters/{chapterId:guid}/manuscript")]
    public async Task<ActionResult<ManuscriptResponseDto>> GetChapterManuscript(Guid chapterId, CancellationToken cancellationToken)
    {
        var chapter = await _db.Chapters.AsNoTracking()
            .Include(c => c.Scenes)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (chapter is null)
            return NotFound();

        var scenes = chapter.Scenes.OrderBy(s => s.Order).ToList();
        var computed = ManuscriptAssembly.AssembleChapter(scenes);
        return Ok(ToDto(chapter.ManuscriptText, computed));
    }

    [HttpPatch("books/{bookId:guid}/manuscript")]
    public async Task<ActionResult<ManuscriptResponseDto>> PatchBookManuscript(Guid bookId, [FromBody] PatchManuscriptBody body,
        CancellationToken cancellationToken)
    {
        var book = await _db.Books
            .Include(b => b.Chapters)
            .ThenInclude(c => c.Scenes)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null)
            return NotFound();

        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var computed = ManuscriptAssembly.AssembleBook(chapters);
        book.ManuscriptText = body.ManuscriptText;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(book.ManuscriptText, computed));
    }

    [HttpPatch("chapters/{chapterId:guid}/manuscript")]
    public async Task<ActionResult<ManuscriptResponseDto>> PatchChapterManuscript(Guid chapterId, [FromBody] PatchManuscriptBody body,
        CancellationToken cancellationToken)
    {
        var chapter = await _db.Chapters
            .Include(c => c.Scenes)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (chapter is null)
            return NotFound();

        var scenes = chapter.Scenes.OrderBy(s => s.Order).ToList();
        var computed = ManuscriptAssembly.AssembleChapter(scenes);
        chapter.ManuscriptText = body.ManuscriptText;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(chapter.ManuscriptText, computed));
    }

    [HttpPost("books/{bookId:guid}/manuscript/assemble")]
    public async Task<ActionResult<ManuscriptResponseDto>> AssembleBookManuscript(Guid bookId, CancellationToken cancellationToken)
    {
        var book = await _db.Books
            .Include(b => b.Chapters)
            .ThenInclude(c => c.Scenes)
            .FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null)
            return NotFound();

        var chapters = book.Chapters.OrderBy(c => c.Order).ToList();
        var computed = ManuscriptAssembly.AssembleBook(chapters);
        book.ManuscriptText = computed;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(book.ManuscriptText, computed));
    }

    [HttpPost("chapters/{chapterId:guid}/manuscript/assemble")]
    public async Task<ActionResult<ManuscriptResponseDto>> AssembleChapterManuscript(Guid chapterId, CancellationToken cancellationToken)
    {
        var chapter = await _db.Chapters
            .Include(c => c.Scenes)
            .FirstOrDefaultAsync(c => c.Id == chapterId, cancellationToken);
        if (chapter is null)
            return NotFound();

        var scenes = chapter.Scenes.OrderBy(s => s.Order).ToList();
        var computed = ManuscriptAssembly.AssembleChapter(scenes);
        chapter.ManuscriptText = computed;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(chapter.ManuscriptText, computed));
    }

    private static ManuscriptResponseDto ToDto(string? stored, string computed)
    {
        var effective = !string.IsNullOrEmpty(stored?.Trim()) ? stored! : computed;
        return new ManuscriptResponseDto
        {
            ManuscriptText = stored,
            ComputedAssembledText = computed,
            EffectiveText = effective
        };
    }

    public sealed class PatchManuscriptBody
    {
        public string? ManuscriptText { get; set; }
    }

    public sealed class ManuscriptResponseDto
    {
        public string? ManuscriptText { get; set; }
        public string ComputedAssembledText { get; set; } = "";
        public string EffectiveText { get; set; } = "";
    }
}
