using System.Text;
using System.Text.RegularExpressions;
using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Agent;

/// <summary>Non-LLM draft checks merged into agent verification.</summary>
public static partial class AgentDeterministicGuards
{
    public sealed record GuardContext(
        string? NarrativePerspective,
        string? NarrativeTense,
        string? ExpectedEndNotes,
        string? StateBeforeJson);

    public static IReadOnlyList<string> AnalyzeCompliance(string draft, GuardContext context)
    {
        var issues = new List<string>();
        issues.AddRange(CheckOpeningStateRecitation(draft, context.StateBeforeJson));
        issues.AddRange(CheckExpectedEndNotes(draft, context.ExpectedEndNotes));
        issues.AddRange(CheckTenseSample(draft, context.NarrativeTense));
        return issues;
    }

    public static IReadOnlyList<string> AnalyzeQuality(string draft, GuardContext context)
    {
        var issues = new List<string>();
        if (CheckOpeningStateRecitation(draft, context.StateBeforeJson).Count > 0)
            issues.Add("Opening reads like a restated state-table inventory instead of dramatized prose.");
        return issues;
    }

    private static List<string> CheckOpeningStateRecitation(string draft, string? stateJson)
    {
        var trimmed = DraftProseGuard.TrimOpeningStateRecitation(draft, stateJson);
        if (string.Equals(trimmed.Trim(), draft.Trim(), StringComparison.Ordinal))
            return [];

        return
        [
            "¶0 appears to restate beginning-state inventory (pose/clothing/mood labels) — dramatize with action or dialogue instead."
        ];
    }

    private static List<string> CheckExpectedEndNotes(string draft, string? expectedEndNotes)
    {
        if (string.IsNullOrWhiteSpace(expectedEndNotes))
            return [];

        var missing = new List<string>();
        foreach (var token in ExtractSignificantTokens(expectedEndNotes))
        {
            if (!draft.Contains(token, StringComparison.OrdinalIgnoreCase))
                missing.Add(token);
        }

        if (missing.Count == 0)
            return [];

        var sample = string.Join(", ", missing.Take(4));
        return [$"Expected end notes may be unmet — no draft match for: {sample}."];
    }

    private static List<string> CheckTenseSample(string draft, string? narrativeTense)
    {
        if (string.IsNullOrWhiteSpace(narrativeTense))
            return [];

        var tense = narrativeTense.Trim().ToLowerInvariant();
        if (!tense.Contains("past", StringComparison.Ordinal) &&
            !tense.Contains("present", StringComparison.Ordinal))
            return [];

        var sample = string.Join(' ', draft.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(120));
        if (sample.Length < 40)
            return [];

        if (tense.Contains("past", StringComparison.Ordinal))
        {
            var presentHits = PresentTensePattern().Matches(sample).Count;
            var pastHits = CommonPastTensePattern().Matches(sample).Count;
            if (presentHits >= 3 && presentHits > pastHits)
                return ["Sampled opening verbs look present-tense while scene specifies past tense."];
        }
        else if (tense.Contains("present", StringComparison.Ordinal))
        {
            var pastHits = CommonPastTensePattern().Matches(sample).Count;
            var presentHits = PresentTensePattern().Matches(sample).Count;
            if (pastHits >= 3 && pastHits > presentHits)
                return ["Sampled opening verbs look past-tense while scene specifies present tense."];
        }

        return [];
    }

    private static IEnumerable<string> ExtractSignificantTokens(string notes)
    {
        foreach (Match m in WordTokenPattern().Matches(notes))
        {
            var word = m.Value;
            if (word.Length >= 5 && char.IsLetter(word[0]))
                yield return word;
        }
    }

    public static string FormatGuardLines(IReadOnlyList<string> issues, string prefix)
    {
        if (issues.Count == 0)
            return "";
        var sb = new StringBuilder();
        sb.AppendLine(prefix);
        foreach (var issue in issues)
            sb.AppendLine($"  • {issue}");
        return sb.ToString().TrimEnd();
    }

    [GeneratedRegex(@"\b(is|are|am|was|were|has|have|had|do|does|did|will|would|can|could|should|must)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PresentTensePattern();

    [GeneratedRegex(@"\b\w+(ed|ied)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CommonPastTensePattern();

    [GeneratedRegex(@"[A-Za-z']{5,}")]
    private static partial Regex WordTokenPattern();
}
