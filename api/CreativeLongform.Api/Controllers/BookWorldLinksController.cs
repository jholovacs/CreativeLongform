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

        string? relationDetail = null;
        if (!string.IsNullOrWhiteSpace(body.RelationDetail))
        {
            relationDetail = body.RelationDetail.Trim();
            if (relationDetail.Length > 4000)
                return BadRequest("relationDetail must be at most 4000 characters.");
        }

        var id = Guid.NewGuid();
        _db.WorldElementLinks.Add(new WorldElementLink
        {
            Id = id,
            FromWorldElementId = fromId,
            ToWorldElementId = toId,
            RelationLabel = body.RelationLabel.Trim(),
            RelationDetail = relationDetail
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
        [FromQuery] Guid? worldElementId = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDesc = false,
        CancellationToken cancellationToken = default)
    {
        if (!await _db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, cancellationToken))
            return NotFound();

        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);

        // Filter on joined entities (not a projected DTO) so EF translates reliably; search titles, summaries, details, and relation text.
        var query = from l in _db.WorldElementLinks.AsNoTracking()
            join f in _db.WorldElements.AsNoTracking() on l.FromWorldElementId equals f.Id
            join t in _db.WorldElements.AsNoTracking() on l.ToWorldElementId equals t.Id
            where f.BookId == bookId && t.BookId == bookId
            select new { l, f, t };

        if (worldElementId is { } weId && weId != Guid.Empty)
        {
            var exists = await _db.WorldElements.AsNoTracking()
                .AnyAsync(w => w.Id == weId && w.BookId == bookId, cancellationToken);
            if (!exists)
                return BadRequest("worldElementId is not an element of this book.");
            query = query.Where(x => x.l.FromWorldElementId == weId || x.l.ToWorldElementId == weId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.f.Title.ToLower().Contains(term) ||
                x.t.Title.ToLower().Contains(term) ||
                x.f.Summary.ToLower().Contains(term) ||
                x.t.Summary.ToLower().Contains(term) ||
                x.f.Detail.ToLower().Contains(term) ||
                x.t.Detail.ToLower().Contains(term) ||
                x.l.RelationLabel.ToLower().Contains(term) ||
                (x.l.RelationDetail != null && x.l.RelationDetail.ToLower().Contains(term)));
        }

        var sortKey = string.IsNullOrWhiteSpace(sortBy) ? "relation" : sortBy.Trim().ToLowerInvariant();
        var ordered = sortKey switch
        {
            "from" when sortDesc => query.OrderByDescending(x => x.f.Title).ThenByDescending(x => x.t.Title)
                .ThenByDescending(x => x.l.RelationLabel),
            "from" => query.OrderBy(x => x.f.Title).ThenBy(x => x.t.Title).ThenBy(x => x.l.RelationLabel),
            "to" when sortDesc => query.OrderByDescending(x => x.t.Title).ThenByDescending(x => x.f.Title)
                .ThenByDescending(x => x.l.RelationLabel),
            "to" => query.OrderBy(x => x.t.Title).ThenBy(x => x.f.Title).ThenBy(x => x.l.RelationLabel),
            "detail" when sortDesc => query.OrderByDescending(x => x.l.RelationDetail ?? string.Empty)
                .ThenByDescending(x => x.f.Title).ThenByDescending(x => x.t.Title),
            "detail" => query.OrderBy(x => x.l.RelationDetail ?? string.Empty).ThenBy(x => x.f.Title).ThenBy(x => x.t.Title),
            _ when sortDesc => query.OrderByDescending(x => x.l.RelationLabel).ThenByDescending(x => x.f.Title)
                .ThenByDescending(x => x.t.Title),
            _ => query.OrderBy(x => x.l.RelationLabel).ThenBy(x => x.f.Title).ThenBy(x => x.t.Title)
        };
        var totalCount = await ordered.CountAsync(cancellationToken);
        var items = await ordered
            .Skip(skip)
            .Take(take)
            .Select(x => new WorldLinkRow
            {
                Id = x.l.Id,
                FromWorldElementId = x.l.FromWorldElementId,
                ToWorldElementId = x.l.ToWorldElementId,
                FromTitle = x.f.Title,
                ToTitle = x.t.Title,
                RelationLabel = x.l.RelationLabel,
                RelationDetail = x.l.RelationDetail
            })
            .ToListAsync(cancellationToken);

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
        public string? RelationDetail { get; set; }
    }

    public sealed class CreateLinkBody
    {
        public Guid FromWorldElementId { get; set; }
        public Guid ToWorldElementId { get; set; }
        public string? RelationLabel { get; set; }
        public string? RelationDetail { get; set; }
    }

    public sealed class LinkCreatedResponse
    {
        public Guid Id { get; set; }
    }
}
