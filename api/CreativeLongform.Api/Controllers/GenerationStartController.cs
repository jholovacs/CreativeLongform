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
        GenerationStartOptions? opts = null;
        if (body is { StopAfterDraft: true } or { MinWordsOverride: not null })
        {
            opts = new GenerationStartOptions
            {
                StopAfterDraft = body.StopAfterDraft,
                MinWordsOverride = body.MinWordsOverride
            };
        }

        var id = await _orchestrator.StartGenerationAsync(sceneId, body?.IdempotencyKey, opts, cancellationToken);
        return Ok(new { id });
    }

    [HttpPost("{sceneId:guid}/generation/{generationRunId:guid}/cancel")]
    public async Task<ActionResult> CancelGeneration(
        Guid sceneId,
        Guid generationRunId,
        CancellationToken cancellationToken)
    {
        var ok = await _orchestrator.CancelGenerationAsync(sceneId, generationRunId, cancellationToken);
        return ok ? NoContent() : NotFound();
    }

    [HttpPost("{sceneId:guid}/generation/finalize")]
    public async Task<ActionResult<FinalizeGenerationResult>> FinalizeGeneration(
        Guid sceneId,
        [FromBody] FinalizeGenerationBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrator.FinalizeGenerationAsync(sceneId, body.GenerationRunId,
                body.AcceptedDraftText, body.ApprovedStateTableJson, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{sceneId:guid}/generation/correct")]
    public async Task<ActionResult> CorrectDraft(
        Guid sceneId,
        [FromBody] CorrectDraftBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            await _orchestrator.CorrectDraftAsync(sceneId, body.GenerationRunId, body.Instruction, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public sealed class StartGenerationBody
    {
        public string? IdempotencyKey { get; set; }
        public bool StopAfterDraft { get; set; }
        public int? MinWordsOverride { get; set; }
    }

    public sealed class FinalizeGenerationBody
    {
        public Guid GenerationRunId { get; set; }
        public string? AcceptedDraftText { get; set; }
        public string? ApprovedStateTableJson { get; set; }
    }

    public sealed class CorrectDraftBody
    {
        public Guid GenerationRunId { get; set; }
        public string Instruction { get; set; } = "";
    }
}
