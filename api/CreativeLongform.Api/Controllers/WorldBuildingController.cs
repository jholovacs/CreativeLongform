using System.Text;
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

    /// <summary>LLM suggests links for one entry vs. others (not persisted).</summary>
    [HttpPost("suggest-links")]
    public async Task<ActionResult<IReadOnlyList<WorldBuildingSuggestedLink>>> SuggestLinks(
        Guid bookId,
        [FromBody] SuggestLinksBody body,
        CancellationToken cancellationToken)
    {
        if (body.ElementId == Guid.Empty)
            return BadRequest("elementId is required.");
        var result = await _worldBuilding.SuggestLinksForElementAsync(bookId, body.ElementId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Creates links the user accepted from the suggestion modal.</summary>
    [HttpPost("apply-suggested-links")]
    public async Task<ActionResult<ApplySuggestedLinksResponse>> ApplySuggestedLinks(
        Guid bookId,
        [FromBody] ApplySuggestedLinksBody body,
        CancellationToken cancellationToken)
    {
        var items = body.Links is { Count: > 0 }
            ? (IReadOnlyList<ApplySuggestedLinkItem>)body.Links
            : Array.Empty<ApplySuggestedLinkItem>();
        var count = await _worldBuilding.ApplySuggestedLinksAsync(bookId, items, cancellationToken);
        return Ok(new ApplySuggestedLinksResponse { CreatedCount = count });
    }

    /// <summary>LLM reviews links and timeline attachments for one world element (not persisted).</summary>
    [HttpPost("elements/{elementId:guid}/review-links-canon")]
    public async Task<ActionResult<LinkCanonReviewResult>> ReviewLinksCanon(
        Guid bookId,
        Guid elementId,
        CancellationToken cancellationToken)
    {
        var result = await _worldBuilding.ReviewLinksCanonAsync(bookId, elementId, cancellationToken);
        return Ok(result);
    }

    /// <summary>Markdown glossary of all world elements (A–Z, articles ignored for sort). Query: useLlm (default true).</summary>
    [HttpGet("glossary-markdown")]
    [Produces("text/markdown")]
    public async Task<IActionResult> GetGlossaryMarkdown(
        Guid bookId,
        [FromQuery] bool useLlm = true,
        CancellationToken cancellationToken = default)
    {
        var md = await _worldBuilding.BuildGlossaryMarkdownAsync(bookId, useLlm, cancellationToken);
        if (md is null)
            return NotFound();
        return Content(md, "text/markdown; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>Apply accepted link/timeline canon review items.</summary>
    [HttpPost("apply-link-canon-review")]
    public async Task<ActionResult<LinkCanonApplyResult>> ApplyLinkCanonReview(
        Guid bookId,
        [FromBody] ApplyLinkCanonBody body,
        CancellationToken cancellationToken)
    {
        var items = body.Items is { Count: > 0 }
            ? (IReadOnlyList<ApplyLinkCanonItem>)body.Items
            : Array.Empty<ApplyLinkCanonItem>();
        var result = await _worldBuilding.ApplyLinkCanonReviewAsync(bookId, items, cancellationToken);
        return Ok(result);
    }

    public sealed class SuggestLinksBody
    {
        public Guid ElementId { get; set; }
    }

    public sealed class ApplySuggestedLinksBody
    {
        public List<ApplySuggestedLinkItem>? Links { get; set; }
    }

    public sealed class ApplySuggestedLinksResponse
    {
        public int CreatedCount { get; set; }
    }

    public sealed class ApplyLinkCanonBody
    {
        public List<ApplyLinkCanonItem>? Items { get; set; }
    }

    [HttpPost("bootstrap")]
    public async Task<ActionResult<WorldBuildingApplyResult>> Bootstrap(
        Guid bookId,
        [FromBody] BootstrapBody body,
        CancellationToken cancellationToken)
    {
        var req = new StoryBootstrapRequest
        {
            StoryToneAndStyle = body.StoryToneAndStyle,
            ContentStyleNotes = body.ContentStyleNotes,
            Synopsis = body.Synopsis,
            SourceText = body.SourceText
        };
        var result = await _worldBuilding.BootstrapStoryAsync(bookId, req, cancellationToken);
        return Ok(result);
    }

    public sealed class BootstrapBody
    {
        public string? StoryToneAndStyle { get; set; }
        public string? ContentStyleNotes { get; set; }
        public string? Synopsis { get; set; }
        public string? SourceText { get; set; }
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
