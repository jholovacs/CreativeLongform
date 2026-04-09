using CreativeLongform.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/scenes")]
public sealed class GenerationStartController : ControllerBase
{
    private readonly IGenerationOrchestrator _orchestrator;

    public GenerationStartController(IGenerationOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    [HttpPost("{sceneId:guid}/generation")]
    public async Task<ActionResult<object>> StartGeneration(
        Guid sceneId,
        [FromBody] StartGenerationBody? body,
        CancellationToken cancellationToken)
    {
        var id = await _orchestrator.StartGenerationAsync(sceneId, body?.IdempotencyKey, cancellationToken);
        return Ok(new { id });
    }

    public sealed class StartGenerationBody
    {
        public string? IdempotencyKey { get; set; }
    }
}
