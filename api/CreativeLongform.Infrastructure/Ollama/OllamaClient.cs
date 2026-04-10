using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Infrastructure.Ollama;

public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _http;
    private readonly IOptions<OllamaOptions> _options;

    public OllamaClient(HttpClient http, IOptions<OllamaOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var res = await _http.GetAsync("tags", cancellationToken);
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<OllamaChatResult> ChatAsync(
        string model,
        IReadOnlyList<OllamaChatMessage> messages,
        bool jsonFormat,
        OllamaChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            ["stream"] = false
        };
        if (jsonFormat)
            payload["format"] = "json";
        if (options?.NumPredict is { } n)
            payload["options"] = new Dictionary<string, object?> { ["num_predict"] = n };

        using var response = await _http.PostAsJsonAsync("chat", payload, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var hint =
                $"Ollama returned 404 for model '{model}' — nothing is registered under that name. " +
                "Check: docker compose exec ollama ollama list. " +
                "Library models (e.g. llama3.2) are installed with: ollama pull <tag>; " +
                "custom GGUF names from setup are created with ollama create (not pull) — if the import failed, re-run make setup or " +
                "align OLLAMA_MODEL in .env with a name that appears in ollama list, then recreate the API container.";
            throw new InvalidOperationException(hint);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama chat failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(errBody, 500)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var content = root.GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
        return new OllamaChatResult(model, content);
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
        return s[..max] + "…";
    }
}
