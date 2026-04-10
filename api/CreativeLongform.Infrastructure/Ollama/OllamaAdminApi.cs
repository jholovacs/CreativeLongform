using System.Net.Http.Json;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using System.Linq;

namespace CreativeLongform.Infrastructure.Ollama;

public sealed class OllamaAdminApi : IOllamaAdminApi
{
    private readonly HttpClient _http;

    public OllamaAdminApi(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Ollama often returns JSON like <c>{"error":"..."}</c>. Surface it clearly and add hints for common storage failures.
    /// </summary>
    private static string FormatOllamaFailure(string operation, int statusCode, string body)
    {
        var trimmed = body.Trim();
        if (trimmed.StartsWith('{') && trimmed.Contains("\"error\""))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
                {
                    var msg = err.GetString() ?? trimmed;
                    return $"{operation} failed ({statusCode}): {msg}{AppendStorageHint(msg)}";
                }
            }
            catch
            {
                /* fall through */
            }
        }

        return $"{operation} failed ({statusCode}): {trimmed}{AppendStorageHint(trimmed)}";
    }

    private static string AppendStorageHint(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "";
        if (message.Contains("input/output error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("no space left", StringComparison.OrdinalIgnoreCase)
            || message.Contains("disk quota", StringComparison.OrdinalIgnoreCase)
            || (message.Contains("max retries exceeded", StringComparison.OrdinalIgnoreCase)
                && message.Contains("partial", StringComparison.OrdinalIgnoreCase)))
        {
            return " — Often: host disk full, Docker/WSL disk limit, or a bad Ollama data volume. Check free space, run `docker system df`, restart Docker, or recreate the `ollama` volume (destroys locally pulled models).";
        }

        return "";
    }

    public async Task<IReadOnlyList<OllamaLocalModelInfo>> ListLocalModelsAsync(CancellationToken cancellationToken = default)
    {
        var vramByName = await GetVramBytesByModelNameAsync(cancellationToken);
        using var response = await _http.GetAsync("tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return Array.Empty<OllamaLocalModelInfo>();
        var list = new List<OllamaLocalModelInfo>();
        foreach (var m in models.EnumerateArray())
        {
            if (!m.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                continue;
            var name = nameEl.GetString()?.Trim();
            if (string.IsNullOrEmpty(name))
                continue;
            long sizeBytes = 0;
            if (m.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var szBytes))
                sizeBytes = szBytes;
            string? parameterSize = null;
            string? quant = null;
            if (m.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object)
            {
                if (details.TryGetProperty("parameter_size", out var ps) && ps.ValueKind == JsonValueKind.String)
                    parameterSize = ps.GetString();
                if (details.TryGetProperty("quantization_level", out var q) && q.ValueKind == JsonValueKind.String)
                    quant = q.GetString();
            }

            long? vram = vramByName.TryGetValue(name, out var v) ? v : null;
            list.Add(new OllamaLocalModelInfo
            {
                Name = name,
                SizeBytes = sizeBytes,
                ParameterSize = parameterSize,
                QuantizationLevel = quant,
                VramBytes = vram
            });
        }

        return list;
    }

    /// <summary>VRAM per model name from <c>GET /api/ps</c> (models currently loaded).</summary>
    private async Task<Dictionary<string, long>> GetVramBytesByModelNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("ps", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return new Dictionary<string, long>(StringComparer.Ordinal);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return new Dictionary<string, long>(StringComparer.Ordinal);
            var dict = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameEl.GetString()?.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;
                if (m.TryGetProperty("size_vram", out var sv) && sv.TryGetInt64(out var vram))
                    dict[name] = vram;
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }
    }

    public async Task<IReadOnlyList<string>> ListModelNamesAsync(CancellationToken cancellationToken = default)
    {
        var models = await ListLocalModelsAsync(cancellationToken);
        return models.Select(m => m.Name).ToList();
    }

    public async Task PullAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name is required.", nameof(modelName));
        using var response = await _http.PostAsJsonAsync(
            "pull",
            new Dictionary<string, object?> { ["name"] = modelName.Trim(), ["stream"] = false },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(FormatOllamaFailure("Ollama pull", (int)response.StatusCode, body));
        }
    }

    public async Task StreamPullAsync(string modelName, Stream output, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name is required.", nameof(modelName));
        using var request = new HttpRequestMessage(HttpMethod.Post, "pull")
        {
            Content = JsonContent.Create(new Dictionary<string, object?>
            {
                ["name"] = modelName.Trim(),
                ["stream"] = true
            })
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(FormatOllamaFailure("Ollama pull", (int)response.StatusCode, body));
        }

        await using var src = await response.Content.ReadAsStreamAsync(cancellationToken);
        await src.CopyToAsync(output, cancellationToken);
    }

    public async Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name is required.", nameof(modelName));
        using var request = new HttpRequestMessage(HttpMethod.Delete, "delete")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["model"] = modelName.Trim() })
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(FormatOllamaFailure("Ollama delete", (int)response.StatusCode, body));
        }
    }

    public async Task CreateFromGgufFileAsync(string modelName, string ggufAbsolutePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            throw new ArgumentException("Model name is required.", nameof(modelName));
        if (string.IsNullOrWhiteSpace(ggufAbsolutePath))
            throw new ArgumentException("GGUF path is required.", nameof(ggufAbsolutePath));
        var path = ggufAbsolutePath.Trim().Replace('\\', '/');
        var modelfile = $"FROM {path}\n";
        using var response = await _http.PostAsJsonAsync(
            "create",
            new Dictionary<string, object?>
            {
                ["name"] = modelName.Trim(),
                ["modelfile"] = modelfile,
                ["stream"] = false
            },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(FormatOllamaFailure("Ollama create", (int)response.StatusCode, body));
        }
    }
}
