using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Agent;

/// <summary>
/// Standard verification pipeline for agent tool results — grounds critic output and applies deterministic guards.
/// </summary>
public static class AgentVerification
{
    public sealed record ComplianceResult(ComplianceVerdict Verdict, IReadOnlyList<string> DroppedItems);

    /// <summary>Ground compliance critic output against the draft, then merge language/script shift findings.</summary>
    public static ComplianceResult ProcessCompliance(string draft, ComplianceVerdict raw, AgentDeterministicGuards.GuardContext? guardContext = null)
    {
        var grounded = ComplianceVerdictGrounding.GroundAgainstDraft(draft, raw);
        var verdict = LanguageContextShiftDetector.MergeIntoCompliance(grounded.Verdict,
            LanguageContextShiftDetector.Analyze(draft));
        verdict = AgentFixInstructionPrioritizer.OrderCompliance(verdict);
        verdict = MergeDeterministicCompliance(draft, verdict, guardContext);
        return new ComplianceResult(verdict, grounded.DroppedItems);
    }

    /// <summary>Merge deterministic language/script shift findings into a quality verdict.</summary>
    public static QualityVerdict ProcessQuality(string draft, QualityVerdict verdict, AgentDeterministicGuards.GuardContext? guardContext = null)
    {
        var merged = LanguageContextShiftDetector.MergeIntoQuality(verdict, LanguageContextShiftDetector.Analyze(draft));
        merged = AgentFixInstructionPrioritizer.OrderQuality(merged);
        return MergeDeterministicQuality(draft, merged, guardContext);
    }

    private static ComplianceVerdict MergeDeterministicCompliance(string draft, ComplianceVerdict verdict, AgentDeterministicGuards.GuardContext? ctx)
    {
        if (ctx is null)
            return verdict;

        var guardIssues = AgentDeterministicGuards.AnalyzeCompliance(draft, ctx);
        if (guardIssues.Count == 0)
            return verdict;

        var violations = verdict.Violations.ToList();
        var fixes = verdict.FixInstructions.ToList();
        foreach (var issue in guardIssues)
        {
            if (violations.Any(v => v.Contains(issue, StringComparison.OrdinalIgnoreCase)))
                continue;
            violations.Add(issue);
            fixes.Add(issue);
        }

        return new ComplianceVerdict
        {
            Pass = false,
            Violations = violations,
            FixInstructions = fixes
        };
    }

    private static QualityVerdict MergeDeterministicQuality(string draft, QualityVerdict verdict, AgentDeterministicGuards.GuardContext? ctx)
    {
        if (ctx is null)
            return verdict;

        var guardIssues = AgentDeterministicGuards.AnalyzeQuality(draft, ctx);
        if (guardIssues.Count == 0)
            return verdict;

        var issues = verdict.Issues.ToList();
        var fixes = verdict.FixInstructions.ToList();
        foreach (var issue in guardIssues)
        {
            if (issues.Any(i => i.Contains(issue, StringComparison.OrdinalIgnoreCase)))
                continue;
            issues.Add(issue);
            fixes.Add(issue);
        }

        return new QualityVerdict
        {
            Score = verdict.Score,
            Issues = issues,
            FixInstructions = fixes
        };
    }
}
