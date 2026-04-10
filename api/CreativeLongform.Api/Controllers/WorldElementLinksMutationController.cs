using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class WorldElementLinksMutationController : ControllerBase
{
    private readonly ICreativeLongformDbContext _db;

    public WorldElementLinksMutationController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [HttpPatch("world-element-links/{linkId:guid}")]
    public async Task<ActionResult> PatchLink(Guid linkId, [FromBody] JsonElement body, CancellationToken cancellationToken)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest("Body must be a JSON object.");

        var hasDetail = body.TryGetProperty("relationDetail", out var propDetail);
        var hasLabel = body.TryGetProperty("relationLabel", out var propLabel);
        if (!hasDetail && !hasLabel)
            return BadRequest("Provide relationDetail and/or relationLabel.");

        var link = await _db.WorldElementLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
            return NotFound();

        if (hasLabel)
        {
            if (propLabel.ValueKind != JsonValueKind.String)
                return BadRequest("relationLabel must be a string.");
            var label = propLabel.GetString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(label))
                return BadRequest("relationLabel cannot be empty.");
            if (label.Length > 128)
                return BadRequest("relationLabel must be at most 128 characters.");
            link.RelationLabel = label;
        }

        if (hasDetail)
        {
            string? relationDetail = null;
            switch (propDetail.ValueKind)
            {
                case JsonValueKind.Null:
                    relationDetail = null;
                    break;
                case JsonValueKind.String:
                    var s = propDetail.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        relationDetail = s.Trim();
                        if (relationDetail.Length > 4000)
                            return BadRequest("relationDetail must be at most 4000 characters.");
                    }
                    else
                        relationDetail = null;
                    break;
                default:
                    return BadRequest("relationDetail must be a string or null.");
            }

            link.RelationDetail = relationDetail;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Conflict("A link with the same endpoints and label may already exist.");
        }

        return NoContent();
    }

    [HttpDelete("world-element-links/{linkId:guid}")]
    public async Task<ActionResult> DeleteLink(Guid linkId, CancellationToken cancellationToken)
    {
        var link = await _db.WorldElementLinks.FirstOrDefaultAsync(l => l.Id == linkId, cancellationToken);
        if (link is null)
            return NotFound();

        _db.WorldElementLinks.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
