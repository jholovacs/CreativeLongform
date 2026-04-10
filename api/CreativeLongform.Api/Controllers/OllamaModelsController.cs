using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/ollama")]
public sealed class OllamaModelsController : ControllerBase
{
    private readonly IOllamaModelPreferencesService _prefs;
    private readonly IOllamaAdminApi _ollamaAdmin;
    private readonly IOptions<OllamaOptions> _options;
    private readonly IHttpClientFactory _httpFactory;

    public OllamaModelsController(
        IOllamaModelPreferencesService prefs,
        IOllamaAdminApi ollamaAdmin,
        IOptions<OllamaOptions> options,
        IHttpClientFactory httpFactory)
    {
        _prefs = prefs;
        _ollamaAdmin = ollamaAdmin;
        _options = options;
        _httpFactory = httpFactory;
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<OllamaPreferencesResponse>> GetPreferences(CancellationToken cancellationToken)
    {
        var assignments = await _prefs.GetAssignmentsAsync(cancellationToken);
        IReadOnlyList<string> installed;
        try
        {
            installed = await _ollamaAdmin.ListModelNamesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Ok(new OllamaPreferencesResponse
            {
                Assignments = assignments,
                InstalledModels = Array.Empty<string>(),
                OllamaListError = ex.Message
            });
        }

        return Ok(new OllamaPreferencesResponse
        {
            Assignments = assignments,
            InstalledModels = installed,
            OllamaListError = null
        });
    }

    [HttpPut("preferences")]
    public async Task<ActionResult<OllamaModelAssignmentsDto>> PutPreferences(
        [FromBody] OllamaModelAssignmentsPatch body,
        CancellationToken cancellationToken)
    {
        var updated = await _prefs.UpdateAssignmentsAsync(body, "ui", cancellationToken);
        return Ok(updated);
    }

    [HttpGet("change-log")]
    public async Task<ActionResult<IReadOnlyList<OllamaModelChangeLogDto>>> GetChangeLog(
        [FromQuery] int take = 80,
        CancellationToken cancellationToken = default)
    {
        var rows = await _prefs.GetChangeLogAsync(take, cancellationToken);
        return Ok(rows);
    }

    [HttpPost("pull")]
    public async Task<ActionResult> Pull([FromBody] PullModelBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Model))
            return BadRequest("model is required.");
        try
        {
            await _ollamaAdmin.PullAsync(body.Model.Trim(), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Ollama returned an error (often disk / volume I/O). Surface message to the UI; not an API bug.
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }

        return NoContent();
    }

    [HttpPost("import-url")]
    public async Task<ActionResult> ImportFromUrl([FromBody] ImportUrlBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Url))
            return BadRequest("url is required.");
        if (string.IsNullOrWhiteSpace(body.ModelName))
            return BadRequest("modelName is required.");

        var staging = _options.Value.ImportStagingDirectory?.Trim() ?? "";
        if (string.IsNullOrEmpty(staging))
            return BadRequest(
                "URL import is not configured. Set Ollama:ImportStagingDirectory to a path shared with the Ollama container (e.g. /shared/import) and mount the same volume on both services.");

        var id = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(staging, id);
        Directory.CreateDirectory(dir);
        var ggufPath = Path.Combine(dir, "model.gguf");

        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromHours(4);
            using var resp = await http.GetAsync(body.Url.Trim(), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(cancellationToken);
            await using var fs = new FileStream(ggufPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(fs, cancellationToken);
        }
        catch
        {
            TryDeleteDir(dir);
            throw;
        }

        var unixPath = ggufPath.Replace('\\', '/');
        try
        {
            await _ollamaAdmin.CreateFromGgufFileAsync(body.ModelName.Trim(), unixPath, cancellationToken);
        }
        finally
        {
            TryDeleteDir(dir);
        }

        return NoContent();
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }

    public sealed class OllamaPreferencesResponse
    {
        public OllamaModelAssignmentsDto Assignments { get; set; } = null!;
        public IReadOnlyList<string> InstalledModels { get; set; } = Array.Empty<string>();
        public string? OllamaListError { get; set; }
    }

    public sealed class PullModelBody
    {
        public string Model { get; set; } = "";
    }

    public sealed class ImportUrlBody
    {
        public string Url { get; set; } = "";
        public string ModelName { get; set; } = "";
    }
}
