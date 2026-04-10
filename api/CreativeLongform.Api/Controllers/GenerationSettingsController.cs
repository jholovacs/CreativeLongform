using CreativeLongform.Application.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class GenerationSettingsController : ControllerBase
{
    private readonly IOptionsSnapshot<OllamaOptions> _ollama;

    public GenerationSettingsController(IOptionsSnapshot<OllamaOptions> ollama)
    {
        _ollama = ollama;
    }

    /// <summary>Server defaults for generation (merge with per-run overrides from the client).</summary>
    [HttpGet("generation")]
    public ActionResult<GenerationSettingsResponse> GetGenerationDefaults()
    {
        var o = _ollama.Value;
        return Ok(new GenerationSettingsResponse(o.QualityAcceptMinScore, o.QualityReviewOnlyMinScore));
    }

    public sealed record GenerationSettingsResponse(double QualityAcceptMinScore, double QualityReviewOnlyMinScore);
}
