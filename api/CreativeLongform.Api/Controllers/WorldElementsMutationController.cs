using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class WorldElementsMutationController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public WorldElementsMutationController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPost("books/{bookId:guid}/world/entries")]
    public async Task<ActionResult<WorldElementCreated>> CreateEntry(
        Guid bookId,
        [FromBody] CreateWorldElementBody body,
        CancellationToken cancellationToken)
    {
        if (!await _db.Books.AsNoTracking().AnyAsync(b => b.Id == bookId, cancellationToken))
            return NotFound();
        if (string.IsNullOrWhiteSpace(body.Title))
            return BadRequest("Title is required.");

        var kind = ParseKind(body.Kind);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        _db.WorldElements.Add(new WorldElement
        {
            Id = id,
            BookId = bookId,
            Kind = kind,
            Title = body.Title.Trim(),
            Slug = string.IsNullOrWhiteSpace(body.Slug) ? null : body.Slug.Trim(),
            Summary = body.Summary?.Trim() ?? string.Empty,
            Detail = body.Detail?.Trim() ?? string.Empty,
            Status = body.Status is { } s && Enum.TryParse<WorldElementStatus>(s, true, out var st) ? st : WorldElementStatus.Draft,
            Provenance = WorldElementProvenance.Manual,
            MetadataJson = null,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new WorldElementCreated { Id = id });
    }

    [HttpPatch("world-elements/{elementId:guid}")]
    public async Task<ActionResult> PatchElement(
        Guid elementId,
        [FromBody] PatchWorldElementBody body,
        CancellationToken cancellationToken)
    {
        var el = await _db.WorldElements.FirstOrDefaultAsync(w => w.Id == elementId, cancellationToken);
        if (el is null)
            return NotFound();

        if (body.Title is not null)
            el.Title = body.Title;
        if (body.Slug is not null)
            el.Slug = body.Slug;
        if (body.Summary is not null)
            el.Summary = body.Summary;
        if (body.Detail is not null)
            el.Detail = body.Detail;
        if (body.Kind is not null)
            el.Kind = ParseKind(body.Kind);
        if (body.Status is not null && Enum.TryParse<WorldElementStatus>(body.Status, true, out var st))
            el.Status = st;
        el.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("world-elements/{elementId:guid}")]
    public async Task<ActionResult> DeleteElement(Guid elementId, CancellationToken cancellationToken)
    {
        var el = await _db.WorldElements.FirstOrDefaultAsync(w => w.Id == elementId, cancellationToken);
        if (el is null)
            return NotFound();

        _db.WorldElements.Remove(el);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static WorldElementKind ParseKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return WorldElementKind.Other;
        return Enum.TryParse<WorldElementKind>(raw, ignoreCase: true, out var k) ? k : WorldElementKind.Other;
    }

    public sealed class CreateWorldElementBody
    {
        public string? Kind { get; set; }
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Detail { get; set; }
        public string? Status { get; set; }
    }

    public sealed class PatchWorldElementBody
    {
        public string? Title { get; set; }
        public string? Slug { get; set; }
        public string? Summary { get; set; }
        public string? Detail { get; set; }
        public string? Kind { get; set; }
        public string? Status { get; set; }
    }

    public sealed class WorldElementCreated
    {
        public Guid Id { get; set; }
    }
}
