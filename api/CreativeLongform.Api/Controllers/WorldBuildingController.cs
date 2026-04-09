using CreativeLongform.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/books/{bookId:guid}/world")]
public sealed class WorldBuildingController : ControllerBase
{
    private readonly IWorldBuildingService _worldBuilding;

    public WorldBuildingController(IWorldBuildingService worldBuilding)
    {
        _worldBuilding = worldBuilding;
    }

    [HttpPost("extract")]
    public async Task<ActionResult<WorldBuildingApplyResult>> Extract(
        Guid bookId,
        [FromBody] TextRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _worldBuilding.ExtractFromTextAsync(bookId, body.Text ?? string.Empty, cancellationToken);
        return Ok(result);
    }

    [HttpPost("generate")]
    public async Task<ActionResult<WorldBuildingApplyResult>> Generate(
        Guid bookId,
        [FromBody] PromptRequest body,
        CancellationToken cancellationToken)
    {
        var result = await _worldBuilding.GenerateFromPromptAsync(bookId, body.Prompt ?? string.Empty, cancellationToken);
        return Ok(result);
    }

    public sealed class TextRequest
    {
        public string? Text { get; set; }
    }

    public sealed class PromptRequest
    {
        public string? Prompt { get; set; }
    }
}
