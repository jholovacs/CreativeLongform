using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Ollama;

namespace CreativeLongform.Infrastructure.Ollama;

public sealed class OllamaClient : IOllamaClient
{
    private readonly HttpClient _http;
    private readonly IOllamaBaseUrlProvider _baseUrl;

    public OllamaClient(HttpClient http, IOllamaBaseUrlProvider baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var apiRoot = await _baseUrl.GetEffectiveBaseUrlAsync(cancellationToken);
            var res = await _http.GetAsync(OllamaBaseUrlHelper.ApiEndpoint(apiRoot, "tags"), cancellationToken);
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
        var apiRoot = await _baseUrl.GetEffectiveBaseUrlAsync(cancellationToken);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            ["stream"] = false
        };
        if (jsonFormat)
            payload["format"] = "json";
        if (options is not null)
        {
            var ollamaOpts = new Dictionary<string, object?>();
            if (options.NumPredict is { } n)
                ollamaOpts["num_predict"] = n;
            if (options.RepeatPenalty is { } rp)
                ollamaOpts["repeat_penalty"] = rp;
            if (options.RepeatLastN is { } rln)
                ollamaOpts["repeat_last_n"] = rln;
            if (options.Temperature is { } temp)
                ollamaOpts["temperature"] = temp;
            if (ollamaOpts.Count > 0)
                payload["options"] = ollamaOpts;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, OllamaBaseUrlHelper.ApiEndpoint(apiRoot, "chat"))
        {
            Content = JsonContent.Create(payload)
        };
        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var hint =
                $"Ollama returned 404 for model '{model}' — nothing is registered under that name on {apiRoot}. " +
                "Check the Ollama models page or run ollama list on that host. " +
                "Library models are installed with ollama pull; custom GGUF names use ollama create.";
            throw new InvalidOperationException(hint);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Ollama chat failed ({apiRoot}): {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(errBody, 500)}");
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
