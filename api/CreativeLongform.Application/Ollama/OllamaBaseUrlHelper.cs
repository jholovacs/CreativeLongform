namespace CreativeLongform.Application.Ollama;

public static class OllamaBaseUrlHelper
{
    private const int OllamaDefaultPort = 11434;

    /// <summary>Normalizes user input to Ollama HTTP API root (…/api).</summary>
    public static string NormalizeApiRoot(string? url)
    {
        var t = url?.Trim() ?? "";
        if (string.IsNullOrEmpty(t))
            return "http://localhost:11434/api";

        if (!t.Contains("://", StringComparison.Ordinal))
            t = "http://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri))
        {
            t = t.TrimEnd('/');
            if (!t.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                t += "/api";
            return t;
        }

        var builder = new UriBuilder(uri);
        if (!HasExplicitPortInInput(t) && builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            builder.Port = OllamaDefaultPort;

        var path = builder.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || path == "/")
            builder.Path = "/api";
        else if (!path.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + "/api";
        else
            builder.Path = path;

        return builder.Uri.ToString().TrimEnd('/');
    }

    public static Uri ApiEndpoint(string apiRoot, string relativePath)
    {
        var root = NormalizeApiRoot(apiRoot).TrimEnd('/');
        var path = relativePath.TrimStart('/');
        return new Uri($"{root}/{path}");
    }

    /// <summary>True when the input authority includes <c>host:port</c> (not scheme-default port).</summary>
    private static bool HasExplicitPortInInput(string input)
    {
        var schemeIdx = input.IndexOf("://", StringComparison.Ordinal);
        if (schemeIdx < 0)
            return false;

        var start = schemeIdx + 3;
        var slash = input.IndexOf('/', start);
        var authority = slash >= 0 ? input[start..slash] : input[start..];

        if (authority.StartsWith('['))
        {
            var close = authority.IndexOf(']');
            return close >= 0 && authority.Length > close + 1 && authority[close + 1] == ':';
        }

        var colon = authority.LastIndexOf(':');
        if (colon <= 0)
            return false;

        var portPart = authority[(colon + 1)..];
        return portPart.Length > 0 && portPart.All(char.IsDigit);
    }
}
