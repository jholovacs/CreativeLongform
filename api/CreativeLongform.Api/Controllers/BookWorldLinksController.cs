using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/books/{bookId:guid}/world")]
public sealed class BookWorldLinksController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public BookWorldLinksController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPost("links")]
    public async Task<ActionResult<LinkCreatedResponse>> CreateLink(
        Guid bookId,
        [FromBody] CreateLinkBody body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.RelationLabel))
            return BadRequest("relationLabel is required.");

        var fromId = body.FromWorldElementId;
        var toId = body.ToWorldElementId;
        if (fromId == Guid.Empty || toId == Guid.Empty || fromId == toId)
            return BadRequest("fromWorldElementId and toWorldElementId must be distinct.");

        var fromOk = await _db.WorldElements.AsNoTracking()
            .AnyAsync(w => w.Id == fromId && w.BookId == bookId, cancellationToken);
        var toOk = await _db.WorldElements.AsNoTracking()
            .AnyAsync(w => w.Id == toId && w.BookId == bookId, cancellationToken);
        if (!fromOk || !toOk)
            return BadRequest("Both elements must belong to this book.");

        var id = Guid.NewGuid();
        _db.WorldElementLinks.Add(new WorldElementLink
        {
            Id = id,
            FromWorldElementId = fromId,
            ToWorldElementId = toId,
            RelationLabel = body.RelationLabel.Trim()
        });
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("A link with the same endpoints and label may already exist.");
        }

        return Ok(new LinkCreatedResponse { Id = id });
    }

    [HttpGet("links")]
    public async Task<ActionResult<WorldLinksPageResponse>> GetLinks(
        Guid bookId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 10,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, cancellationToken))
            return NotFound();

        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);

        var query = from l in _db.WorldElementLinks.AsNoTracking()
            join f in _db.WorldElements.AsNoTracking() on l.FromWorldElementId equals f.Id
            join t in _db.WorldElements.AsNoTracking() on l.ToWorldElementId equals t.Id
            where f.BookId == bookId && t.BookId == bookId
            select new WorldLinkRow
            {
                Id = l.Id,
                FromWorldElementId = l.FromWorldElementId,
                ToWorldElementId = l.ToWorldElementId,
                FromTitle = f.Title,
                ToTitle = t.Title,
                RelationLabel = l.RelationLabel
            };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.FromTitle.ToLowerInvariant().Contains(term) ||
                x.ToTitle.ToLowerInvariant().Contains(term) ||
                x.RelationLabel.ToLowerInvariant().Contains(term));
        }

        var ordered = query.OrderBy(x => x.RelationLabel).ThenBy(x => x.FromTitle).ThenBy(x => x.ToTitle);
        var totalCount = await ordered.CountAsync(cancellationToken);
        var items = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return Ok(new WorldLinksPageResponse { TotalCount = totalCount, Items = items });
    }

    public sealed class WorldLinksPageResponse
    {
        public int TotalCount { get; set; }
        public List<WorldLinkRow> Items { get; set; } = new();
    }

    public sealed class WorldLinkRow
    {
        public Guid Id { get; set; }
        public Guid FromWorldElementId { get; set; }
        public Guid ToWorldElementId { get; set; }
        public string FromTitle { get; set; } = string.Empty;
        public string ToTitle { get; set; } = string.Empty;
        public string RelationLabel { get; set; } = string.Empty;
    }

    public sealed class CreateLinkBody
    {
        public Guid FromWorldElementId { get; set; }
        public Guid ToWorldElementId { get; set; }
        public string? RelationLabel { get; set; }
    }

    public sealed class LinkCreatedResponse
    {
        public Guid Id { get; set; }
    }
}
