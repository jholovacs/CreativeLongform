using System.Text;
using System.Text.RegularExpressions;

namespace CreativeLongform.Application.Generation;

/// <summary>Filters compliance critic output that echoes prompt rules or cites text absent from the draft.</summary>
public static class ComplianceVerdictGrounding
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex QuoteRegex = new(
        @"""([^""]+)""|'([^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex ParagraphRefRegex = new(
        @"(?:¶|paragraph\s*)(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly string[] PromptEchoFragments =
    [
        "invented characters or relationships",
        "plot events not grounded in the scene synopsis",
        "not in the book-level synopsis alone",
        "not grounded in the scene synopsis/instructions",
        "statebefore, and linked world-building",
        "stateBefore, and linked world-building",
        "do not list characters as \"unintroduced\"",
        "change \"he were\" to \"he was\"",
        "add closing quote after",
        "said mara",
    ];

    public sealed record GroundingResult(ComplianceVerdict Verdict, IReadOnlyList<string> DroppedItems);

    public static GroundingResult GroundAgainstDraft(string draft, ComplianceVerdict raw)
    {
        if (raw.Pass)
            return new GroundingResult(raw, Array.Empty<string>());

        var normalizedDraft = NormalizeForSearch(draft);
        var dropped = new List<string>();
        var keptViolations = new List<string>();
        var keptFixes = new List<string>();

        foreach (var v in raw.Violations)
        {
            if (ShouldDropItem(v, normalizedDraft, requireQuotedEvidence: false, dropped, "violation"))
                continue;
            keptViolations.Add(v.Trim());
        }

        foreach (var f in raw.FixInstructions)
        {
            if (ShouldDropItem(f, normalizedDraft, requireQuotedEvidence: true, dropped, "fixInstruction"))
                continue;
            keptFixes.Add(f.Trim());
        }

        var pass = keptViolations.Count == 0 && keptFixes.Count == 0;
        var verdict = new ComplianceVerdict
        {
            Pass = pass,
            Violations = keptViolations,
            FixInstructions = keptFixes
        };
        return new GroundingResult(verdict, dropped);
    }

    private static bool ShouldDropItem(
        string item,
        string normalizedDraft,
        bool requireQuotedEvidence,
        List<string> dropped,
        string kind)
    {
        var trimmed = item.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            dropped.Add($"{kind} (empty)");
            return true;
        }

        if (IsPromptEcho(trimmed))
        {
            dropped.Add($"{kind} (prompt/rule echo): {trimmed}");
            return true;
        }

        if (IsGenericCategoryOnly(trimmed))
        {
            dropped.Add($"{kind} (generic category, no draft quote): {trimmed}");
            return true;
        }

        var quotes = ExtractQuotedStrings(trimmed);
        if (quotes.Count > 0)
        {
            if (!quotes.Any(q => TextExistsInDraft(normalizedDraft, q)))
            {
                dropped.Add($"{kind} (quoted text not in draft): {trimmed}");
                return true;
            }

            return false;
        }

        if (requireQuotedEvidence)
        {
            dropped.Add($"{kind} (no quotable draft excerpt): {trimmed}");
            return true;
        }

        // Violations may describe location-only issues if paragraph reference is valid and non-generic.
        if (TryExtractParagraphIndex(trimmed, out var paraIdx) && paraIdx >= 0)
            return false;

        if (trimmed.Length < 24)
        {
            dropped.Add($"{kind} (too vague): {trimmed}");
            return true;
        }

        return false;
    }

    public static bool TextExistsInDraft(string normalizedDraft, string phrase)
    {
        var normalizedPhrase = NormalizeForSearch(phrase);
        if (normalizedPhrase.Length < 2)
            return false;
        return normalizedDraft.Contains(normalizedPhrase, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> ExtractQuotedStrings(string text)
    {
        var results = new List<string>();
        foreach (Match m in QuoteRegex.Matches(text))
        {
            var inner = (m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            if (inner.Length >= 2 && !IsPlaceholderQuote(inner))
                results.Add(inner);
        }

        return results;
    }

    private static bool IsPromptEcho(string text)
    {
        var lower = text.ToLowerInvariant();
        return PromptEchoFragments.Any(f => lower.Contains(f, StringComparison.Ordinal));
    }

    private static bool IsGenericCategoryOnly(string text)
    {
        var lower = text.ToLowerInvariant();
        if (ExtractQuotedStrings(text).Count > 0)
            return false;

        return lower is "invented characters or relationships"
            or "invented characters"
            or "wrong pov"
            or "wrong tense"
            or "fix grammar"
            or "improve punctuation"
            || (lower.StartsWith("invented ", StringComparison.Ordinal) && lower.Length < 80)
            || (lower.Contains("not grounded", StringComparison.Ordinal) && !lower.Contains('¶') && text.Length < 120);
    }

    private static bool IsPlaceholderQuote(string inner)
    {
        var lower = inner.ToLowerInvariant();
        return lower is "exact words from draft" or "corrected form"
            || lower.Contains("…")
            || lower.Contains("...");
    }

    private static bool TryExtractParagraphIndex(string text, out int paragraphIndex)
    {
        paragraphIndex = -1;
        var m = ParagraphRefRegex.Match(text);
        if (!m.Success)
            return false;
        if (!int.TryParse(m.Groups[1].Value, out var n))
            return false;
        paragraphIndex = n;
        return true;
    }

    private static string NormalizeForSearch(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
