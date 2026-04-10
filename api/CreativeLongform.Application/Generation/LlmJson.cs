using System.Collections.Generic;
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

    /// <summary>
    /// Parses compliance JSON. Empty <c>{}</c> deserializes with <c>pass: false</c> by default — we treat missing/empty
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
