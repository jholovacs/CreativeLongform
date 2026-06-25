using System.Linq;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Ollama;
using CreativeLongform.Application.Options;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Api.Controllers;

[ApiController]
[Route("api/ollama")]
public sealed class OllamaModelsController : ControllerBase
{
    private readonly IOllamaModelPreferencesService _prefs;
    private readonly IOllamaAdminApi _ollamaAdmin;
    private readonly IOllamaBaseUrlProvider _baseUrl;
    private readonly IOptions<OllamaOptions> _options;
    private readonly IHttpClientFactory _httpFactory;

    public OllamaModelsController(
        IOllamaModelPreferencesService prefs,
        IOllamaAdminApi ollamaAdmin,
        IOllamaBaseUrlProvider baseUrl,
        IOptions<OllamaOptions> options,
        IHttpClientFactory httpFactory)
    {
        _prefs = prefs;
        _ollamaAdmin = ollamaAdmin;
        _baseUrl = baseUrl;
        _options = options;
        _httpFactory = httpFactory;
    }

    [HttpGet("preferences")]
    public async Task<ActionResult<OllamaPreferencesResponse>> GetPreferences(CancellationToken cancellationToken)
    {
        var assignments = await _prefs.GetAssignmentsAsync(cancellationToken);
        var connection = await _baseUrl.GetConnectionSettingsAsync(cancellationToken);
        var diskSpace = TryGetDiskSpace(_options.Value);
        IReadOnlyList<OllamaLocalModelInfo> installed;
        try
        {
            installed = await _ollamaAdmin.ListLocalModelsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            return Ok(new OllamaPreferencesResponse
            {
                Assignments = assignments,
                Connection = connection,
                InstalledModels = Array.Empty<OllamaInstalledModelDto>(),
                OllamaListError = ex.Message,
                DiskSpace = diskSpace
            });
        }

        var installedDtos = installed
            .Select(m => new OllamaInstalledModelDto
            {
                Name = m.Name,
                SizeBytes = m.SizeBytes,
                ParameterSize = m.ParameterSize,
                QuantizationLevel = m.QuantizationLevel,
                VramBytes = m.VramBytes
            })
            .ToList();

        return Ok(new OllamaPreferencesResponse
        {
            Assignments = assignments,
            Connection = connection,
            InstalledModels = installedDtos,
            OllamaListError = null,
            DiskSpace = diskSpace
        });
    }

    [HttpPost("test-connection")]
    public async Task<ActionResult<OllamaTestConnectionResponse>> TestConnection(
        [FromBody] TestConnectionBody? body,
        CancellationToken cancellationToken)
    {
        var configured = await _baseUrl.GetConnectionSettingsAsync(cancellationToken);
        var apiRoot = string.IsNullOrWhiteSpace(body?.BaseUrl)
            ? configured.EffectiveBaseUrl
            : OllamaBaseUrlHelper.NormalizeApiRoot(body.BaseUrl);
        try
        {
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            var res = await http.GetAsync(OllamaBaseUrlHelper.ApiEndpoint(apiRoot, "tags"), cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync(cancellationToken);
                return Ok(new OllamaTestConnectionResponse
                {
                    BaseUrl = apiRoot,
                    Connected = false,
                    Error = $"HTTP {(int)res.StatusCode}: {err.Trim()}"
                });
            }

            return Ok(new OllamaTestConnectionResponse { BaseUrl = apiRoot, Connected = true });
        }
        catch (Exception ex)
        {
            return Ok(new OllamaTestConnectionResponse
            {
                BaseUrl = apiRoot,
                Connected = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Free/total space for the filesystem containing <see cref="OllamaOptions.DiskSpaceCheckPath"/> or
    /// <see cref="OllamaOptions.ImportStagingDirectory"/>.
    /// </summary>
    private static OllamaDiskSpaceDto? TryGetDiskSpace(OllamaOptions o)
    {
        var path = !string.IsNullOrWhiteSpace(o.DiskSpaceCheckPath?.Trim())
            ? o.DiskSpaceCheckPath.Trim()
            : o.ImportStagingDirectory?.Trim() ?? "";
        if (string.IsNullOrEmpty(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path);
            var dir = new DirectoryInfo(full);
            while (!dir.Exists && dir.Parent != null)
                dir = dir.Parent;
            if (!dir.Exists)
                return null;
            var drive = new DriveInfo(dir.FullName);
            if (!drive.IsReady)
                return null;
            return new OllamaDiskSpaceDto
            {
                PathChecked = path,
                BytesFree = drive.AvailableFreeSpace,
                BytesTotal = drive.TotalSize
            };
        }
        catch
        {
            return null;
        }
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

    /// <summary>
    /// Stream a library pull with <see cref="IOllamaAdminApi.StreamPullAsync"/> (NDJSON lines) for live progress in the UI.
    /// </summary>
    [HttpPost("pull/stream")]
    public async Task<IActionResult> PullStream([FromBody] PullModelBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Model))
            return BadRequest("model is required.");
        try
        {
            Response.ContentType = "application/x-ndjson; charset=utf-8";
            Response.Headers.CacheControl = "no-store";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await _ollamaAdmin.StreamPullAsync(body.Model.Trim(), Response.Body, cancellationToken);
            return new EmptyResult();
        }
        catch (InvalidOperationException ex)
        {
            if (Response.HasStarted)
                return new EmptyResult();
            return StatusCode(StatusCodes.Status502BadGateway, ex.Message);
        }
    }

    /// <summary>Remove a model from the Ollama host disk (free space).</summary>
    [HttpPost("models/delete")]
    public async Task<ActionResult> DeleteModel([FromBody] DeleteModelBody body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body.Model))
            return BadRequest("model is required.");
        try
        {
            await _ollamaAdmin.DeleteModelAsync(body.Model.Trim(), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
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

        var connection = await _baseUrl.GetConnectionSettingsAsync(cancellationToken);
        if (!IsLikelyCoLocatedOllama(connection.EffectiveBaseUrl))
            return BadRequest(
                "GGUF URL import requires Ollama on the same host as the API (shared staging volume). " +
                "For a remote Ollama machine, pull library models or import GGUF on that host directly, then assign model names here.");

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

    private static bool IsLikelyCoLocatedOllama(string apiRoot)
    {
        if (!Uri.TryCreate(apiRoot, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals("127.0.0.1", StringComparison.Ordinal)
               || host.Equals("ollama", StringComparison.OrdinalIgnoreCase)
               || host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class OllamaPreferencesResponse
    {
        public OllamaModelAssignmentsDto Assignments { get; set; } = null!;
        public OllamaConnectionSettingsDto Connection { get; set; } = null!;
        public IReadOnlyList<OllamaInstalledModelDto> InstalledModels { get; set; } = Array.Empty<OllamaInstalledModelDto>();
        public string? OllamaListError { get; set; }
        /// <summary>Null when no path is configured or space could not be read.</summary>
        public OllamaDiskSpaceDto? DiskSpace { get; set; }
    }

    public sealed class OllamaDiskSpaceDto
    {
        public string PathChecked { get; set; } = "";
        public long BytesFree { get; set; }
        public long BytesTotal { get; set; }
    }

    public sealed class OllamaInstalledModelDto
    {
        public string Name { get; set; } = "";
        public long SizeBytes { get; set; }
        public string? ParameterSize { get; set; }
        public string? QuantizationLevel { get; set; }
        /// <summary>VRAM while loaded (<c>GET /api/ps</c>); null when not in memory.</summary>
        public long? VramBytes { get; set; }
    }

    public sealed class PullModelBody
    {
        public string Model { get; set; } = "";
    }

    public sealed class DeleteModelBody
    {
        public string Model { get; set; } = "";
    }

    public sealed class ImportUrlBody
    {
        public string Url { get; set; } = "";
        public string ModelName { get; set; } = "";
    }

    public sealed class TestConnectionBody
    {
        /// <summary>Optional URL to test before saving; uses effective URL when omitted.</summary>
        public string? BaseUrl { get; set; }
    }

    public sealed class OllamaTestConnectionResponse
    {
        public string BaseUrl { get; set; } = "";
        public bool Connected { get; set; }
        public string? Error { get; set; }
    }
}
