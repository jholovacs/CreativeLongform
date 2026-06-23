using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CreativeLongform.Application.Generation;

public static class LlmJson
{
    /// <summary>Returns true when the payload is a JSON object with no properties (after stripping markdown fences).</summary>
    public static bool IsEmptyJsonObject(string text)
    {
        var cleaned = StripMarkdownFences(text).Trim();
        if (string.IsNullOrEmpty(cleaned))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var _ in root.EnumerateObject())
                return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Returns the first non-empty JSON object among candidates, or null.</summary>
    public static string? FirstUsableStateJson(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryNormalizeStateJson(candidate, out var normalized))
                return normalized;
        }

        return null;
    }

    /// <summary>
    /// Parses narrative state JSON from LLM output (markdown fences, leading prose) and returns compact JSON for jsonb storage.
    /// </summary>
    public static bool TryNormalizeStateJson(string? text, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = StripMarkdownFences(text).Trim();
        cleaned = ExtractJsonObject(cleaned);
        if (string.IsNullOrWhiteSpace(cleaned))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (!doc.RootElement.EnumerateObject().Any())
                return false;
            normalized = JsonSerializer.Serialize(doc.RootElement, JsonOptions.Default);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string NormalizeStateJsonOrThrow(string text, string context)
    {
        if (TryNormalizeStateJson(text, out var normalized))
            return normalized;
        throw new InvalidOperationException($"{context} was not valid JSON.");
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
            return text;

        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return text[start..];
    }

    /// <summary>Parses compliance JSON. Empty <c>{}</c> deserializes with <c>pass: false</c> by default — we treat missing/empty
    /// payloads as pass with no violations unless issues are listed without <c>pass</c>.
    /// Prefer <see cref="IsEmptyJsonObject"/> + retry at the call site before relying on this for <c>{}</c>.
    /// </summary>
    public static ComplianceVerdict DeserializeComplianceVerdict(string text)
    {
        var cleaned = StripMarkdownFences(text).Trim();
        if (string.IsNullOrEmpty(cleaned))
        {
            return new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() };
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() };
            }

            var v = JsonSerializer.Deserialize<ComplianceVerdict>(cleaned, JsonOptions.Default)
                    ?? new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() };
            v.Violations ??= new List<string>();
            v.FixInstructions ??= new List<string>();

            var hasPassProperty = false;
            foreach (var p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, "pass", StringComparison.OrdinalIgnoreCase))
                {
                    hasPassProperty = true;
                    break;
                }
            }

            if (!hasPassProperty)
            {
                if (v.Violations.Count == 0 && v.FixInstructions.Count == 0)
                    v.Pass = true;
                else
                    v.Pass = false;
            }

            return v;
        }
        catch (JsonException)
        {
            return new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() };
        }
    }

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
