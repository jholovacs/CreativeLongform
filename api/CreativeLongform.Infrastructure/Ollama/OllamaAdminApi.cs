using System.Net.Http.Json;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;

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

    public async Task<IReadOnlyList<string>> ListModelNamesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        if (!root.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var m in models.EnumerateArray())
        {
            if (m.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                var s = name.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s.Trim());
            }
        }

        return list;
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
