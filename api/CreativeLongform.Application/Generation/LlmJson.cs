using System.Text.Json;

namespace CreativeLongform.Application.Generation;

public static class LlmJson
{
    public static string StripMarkdownFences(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal))
            return t;

        var firstNl = t.IndexOf('\n');
        if (firstNl < 0)
            return t;
        t = t[(firstNl + 1)..];
        var end = t.LastIndexOf("```", StringComparison.Ordinal);
        if (end > 0)
            t = t[..end];
        return t.Trim();
    }

    public static T? Deserialize<T>(string text, JsonSerializerOptions? options = null)
    {
        var cleaned = StripMarkdownFences(text);
        return JsonSerializer.Deserialize<T>(cleaned, options ?? JsonOptions.Default);
    }
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
