using System.Text;

namespace CreativeLongform.Application.Agent;

/// <summary>Lightweight post-delegation instruction adherence hints for the orchestrator.</summary>
public static class AgentDelegationVerifier
{
    public static string Assess(string instruction, string originalSpan, string replacement)
    {
        var sb = new StringBuilder();
        sb.AppendLine("delegation_verification:");

        if (string.Equals(Normalize(originalSpan), Normalize(replacement), StringComparison.Ordinal))
        {
            sb.AppendLine("  warning: delegated output is identical to input — instruction may not have been applied.");
            return sb.ToString().TrimEnd();
        }

        var lower = instruction.ToLowerInvariant();
        if (lower.Contains("past tense", StringComparison.Ordinal) &&
            CountMatches(replacement, @"\b(is|are|am|was|were)\b") > CountMatches(originalSpan, @"\b(is|are|am|was|were)\b"))
            sb.AppendLine("  note: present-tense linking verbs increased — verify past tense was applied.");

        if (lower.Contains("present tense", StringComparison.Ordinal) &&
            CountMatches(replacement, @"\b\w+ed\b") > CountMatches(originalSpan, @"\b\w+ed\b") + 2)
            sb.AppendLine("  note: past-tense verb forms increased — verify present tense was applied.");

        var origWords = WordSet(originalSpan);
        var newWords = WordSet(replacement);
        var overlap = origWords.Intersect(newWords, StringComparer.OrdinalIgnoreCase).Count();
        var union = origWords.Union(newWords, StringComparer.OrdinalIgnoreCase).Count();
        if (union > 0 && (double)overlap / union < 0.35)
            sb.AppendLine("  note: large vocabulary shift — re-read ¶ and confirm plot beats were preserved.");

        if (sb.Length <= "delegation_verification:\n".Length)
            sb.AppendLine("  ok: span changed; re-read and run_compliance_check if this was a substantive edit.");

        return sb.ToString().TrimEnd();
    }

    private static int CountMatches(string text, string pattern) =>
        System.Text.RegularExpressions.Regex.Matches(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

    private static HashSet<string> WordSet(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
