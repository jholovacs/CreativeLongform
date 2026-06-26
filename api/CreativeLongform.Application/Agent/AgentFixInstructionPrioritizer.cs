using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Agent;

/// <summary>Orders critic fix items so the agent addresses high-impact issues first.</summary>
public static class AgentFixInstructionPrioritizer
{
    private static readonly string[] HighPriorityKeywords =
    [
        "wrong ending", "ending", "synopsis", "instruction", "invented", "contradict",
        "canon", "scope", "plot", "character not", "unauthorized", "expected end"
    ];

    private static readonly string[] MediumPriorityKeywords =
    [
        "pov", "perspective", "tense", "voice", "dramatiz", "show", "tell", "language", "script"
    ];

    public static ComplianceVerdict OrderCompliance(ComplianceVerdict verdict)
    {
        if (verdict.FixInstructions.Count <= 1 && verdict.Violations.Count <= 1)
            return verdict;

        return new ComplianceVerdict
        {
            Pass = verdict.Pass,
            Violations = OrderStrings(verdict.Violations),
            FixInstructions = OrderStrings(verdict.FixInstructions)
        };
    }

    public static QualityVerdict OrderQuality(QualityVerdict verdict)
    {
        if (verdict.FixInstructions.Count <= 1 && verdict.Issues.Count <= 1)
            return verdict;

        return new QualityVerdict
        {
            Score = verdict.Score,
            Issues = OrderStrings(verdict.Issues),
            FixInstructions = OrderStrings(verdict.FixInstructions)
        };
    }

    private static List<string> OrderStrings(IReadOnlyList<string> items)
    {
        return items
            .Select((text, index) => (text, index, rank: Score(text)))
            .OrderBy(x => x.rank)
            .ThenBy(x => x.index)
            .Select(x => x.text)
            .ToList();
    }

    private static int Score(string text)
    {
        var lower = text.ToLowerInvariant();
        if (HighPriorityKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
            return 0;
        if (MediumPriorityKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
            return 1;
        return 2;
    }
}
