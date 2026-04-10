using System.Net.Http;
using System.Text.Json;
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
        if (body is not null)
        {
            opts = new GenerationStartOptions
            {
                StopAfterDraft = body.StopAfterDraft,
                MinWordsOverride = body.MinWordsOverride,
                MaxWordsOverride = body.MaxWordsOverride,
                SkipQualityGate = body.SkipQualityGate,
                QualityAcceptMinScore = body.QualityAcceptMinScore,
                QualityReviewOnlyMinScore = body.QualityReviewOnlyMinScore
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
        [FromBody] FinalizeGenerationBody? body,
        CancellationToken cancellationToken)
    {
        if (body is null)
            return BadRequest("Request body is required.");

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
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Language model request failed. Ensure Ollama is running and reachable at the configured URL.", detail = ex.Message });
        }
        catch (JsonException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "Invalid JSON from the language model.", detail = ex.Message });
        }
    }

    [HttpPost("{sceneId:guid}/generation/correct")]
    public async Task<ActionResult<CorrectDraftResult>> CorrectDraft(
        Guid sceneId,
        [FromBody] CorrectDraftBody body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orchestrator.CorrectDraftAsync(sceneId, body.GenerationRunId, body.Instruction,
                body.CurrentDraftText, body.SelectionStart, body.SelectionEnd, cancellationToken);
            return Ok(result);
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
        public int? MaxWordsOverride { get; set; }
        /// <summary>Skips the LLM prose quality gate (compliance still runs).</summary>
        public bool SkipQualityGate { get; set; }
        /// <summary>0–100; overrides server default for this run.</summary>
        public double? QualityAcceptMinScore { get; set; }
        /// <summary>0–100; overrides server default for this run.</summary>
        public double? QualityReviewOnlyMinScore { get; set; }
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
        /// <summary>Full draft from the editor; when set, used instead of the server copy.</summary>
        public string? CurrentDraftText { get; set; }
        /// <summary>Start index (UTF-16), inclusive. With <see cref="SelectionEnd"/>, only this range is replaced.</summary>
        public int? SelectionStart { get; set; }
        /// <summary>End index (UTF-16), exclusive (same as HTML textarea).</summary>
        public int? SelectionEnd { get; set; }
    }
}
