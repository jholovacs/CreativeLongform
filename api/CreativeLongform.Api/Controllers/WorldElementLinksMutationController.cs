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
