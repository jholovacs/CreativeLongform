using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class TimelineMutationController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public TimelineMutationController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPost("books/{bookId:guid}/timeline/world-events")]
    public async Task<ActionResult<TimelineEntryCreated>> CreateWorldEvent(
        Guid bookId,
        [FromBody] CreateWorldTimelineEventBody body,
        CancellationToken cancellationToken)
    {
        if (!await _db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, cancellationToken))
            return NotFound();
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest("Title is required.");

        decimal sortKey;
        if (body.SortKey is { } sk)
            sortKey = sk;
        else
        {
            var max = await _db.TimelineEntries.Where(t => t.BookId == bookId).MaxAsync(t => (decimal?)t.SortKey, cancellationToken);
            sortKey = (max ?? 0m) + 1000m;
        }

        if (body.WorldElementId is { } weIdCreate &&
            !await _db.WorldElements.AsNoTracking().AnyAsync(w => w.Id == weIdCreate && w.BookId == bookId, cancellationToken))
            return BadRequest("WorldElementId must belong to this book.");

        var id = Guid.NewGuid();
        _db.TimelineEntries.Add(new TimelineEntry
        {
            Id = id,
            BookId = bookId,
            Kind = TimelineEntryKind.WorldEvent,
            SortKey = sortKey,
            SceneId = null,
            Title = body.Title.Trim(),
            Summary = string.IsNullOrWhiteSpace(body.Summary) ? null : body.Summary.Trim(),
            WorldElementId = body.WorldElementId,
            CurrencyPairBase = NormalizeNullable(body.CurrencyPairBase),
            CurrencyPairQuote = NormalizeNullable(body.CurrencyPairQuote),
            CurrencyPairAuthority = NormalizeNullable(body.CurrencyPairAuthority),
            CurrencyPairExchangeNote = NormalizeNullable(body.CurrencyPairExchangeNote)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new TimelineEntryCreated { Id = id });
    }

    [HttpPatch("timeline-entries/{entryId:guid}")]
    public async Task<ActionResult> PatchEntry(
        Guid entryId,
        [FromBody] PatchTimelineEntryBody body,
        CancellationToken cancellationToken)
    {
        var entry = await _db.TimelineEntries.FirstOrDefaultAsync(t => t.Id == entryId, cancellationToken);
        if (entry is null)
            return NotFound();

        if (body.SortKey is { } sk)
            entry.SortKey = sk;
        if (body.Title is not null)
            entry.Title = body.Title;
        if (body.Summary is not null)
            entry.Summary = body.Summary;
        if (body.WorldElementId is Guid weId)
        {
            if (!await _db.WorldElements.AsNoTracking().AnyAsync(w => w.Id == weId && w.BookId == entry.BookId, cancellationToken))
                return BadRequest("WorldElementId must belong to this book.");
            entry.WorldElementId = weId;
        }
        else if (body.WorldElementId is null && body.ClearWorldElementId == true)
            entry.WorldElementId = null;

        if (body.ClearCurrencyPair == true)
        {
            entry.CurrencyPairBase = null;
            entry.CurrencyPairQuote = null;
            entry.CurrencyPairAuthority = null;
            entry.CurrencyPairExchangeNote = null;
        }
        else
        {
            if (body.CurrencyPairBase is not null)
                entry.CurrencyPairBase = NormalizeNullable(body.CurrencyPairBase);
            if (body.CurrencyPairQuote is not null)
                entry.CurrencyPairQuote = NormalizeNullable(body.CurrencyPairQuote);
            if (body.CurrencyPairAuthority is not null)
                entry.CurrencyPairAuthority = NormalizeNullable(body.CurrencyPairAuthority);
            if (body.CurrencyPairExchangeNote is not null)
                entry.CurrencyPairExchangeNote = NormalizeNullable(body.CurrencyPairExchangeNote);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static string? NormalizeNullable(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [HttpDelete("timeline-entries/{entryId:guid}")]
    public async Task<ActionResult> DeleteEntry(Guid entryId, CancellationToken cancellationToken)
    {
        var entry = await _db.TimelineEntries.FirstOrDefaultAsync(t => t.Id == entryId, cancellationToken);
        if (entry is null)
            return NotFound();
        if (entry.Kind == TimelineEntryKind.Scene)
            return BadRequest("Scene timeline rows are removed when the scene is deleted.");

        _db.TimelineEntries.Remove(entry);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public sealed class CreateWorldTimelineEventBody
    {
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public decimal? SortKey { get; set; }
        public Guid? WorldElementId { get; set; }
        public string? CurrencyPairBase { get; set; }
        public string? CurrencyPairQuote { get; set; }
        public string? CurrencyPairAuthority { get; set; }
        public string? CurrencyPairExchangeNote { get; set; }
    }

    public sealed class PatchTimelineEntryBody
    {
        public decimal? SortKey { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public Guid? WorldElementId { get; set; }
        public bool? ClearWorldElementId { get; set; }
        public string? CurrencyPairBase { get; set; }
        public string? CurrencyPairQuote { get; set; }
        public string? CurrencyPairAuthority { get; set; }
        public string? CurrencyPairExchangeNote { get; set; }
        public bool? ClearCurrencyPair { get; set; }
    }

    public sealed class TimelineEntryCreated
    {
        public Guid Id { get; set; }
    }
}
