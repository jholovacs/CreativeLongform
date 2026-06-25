namespace CreativeLongform.Application.Ollama;

public static class OllamaBaseUrlHelper
{
    /// <summary>Normalizes user input to Ollama HTTP API root (…/api).</summary>
    public static string NormalizeApiRoot(string? url)
    {
        var t = url?.Trim().TrimEnd('/') ?? "";
        if (string.IsNullOrEmpty(t))
            return "http://localhost:11434/api";
        if (!t.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            t += "/api";
        return t;
    }

    public static Uri ApiEndpoint(string apiRoot, string relativePath)
    {
        var root = NormalizeApiRoot(apiRoot).TrimEnd('/');
        var path = relativePath.TrimStart('/');
        return new Uri($"{root}/{path}");
    }
}
