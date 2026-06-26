using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Tests;

public sealed class LanguageContextShiftDetectorTests
{
    [Fact]
    public void Analyze_pure_english_draft_has_no_shift()
    {
        const string draft = """
            She opened the door and stepped into the rain.

            The harbor smelled of tar and salt. Ships groaned against the pilings in the dark.
            """;

        var analysis = LanguageContextShiftDetector.Analyze(draft);

        Assert.False(analysis.HasShift);
        Assert.Equal("Latin", analysis.BaselineScript);
        Assert.Empty(analysis.Findings);
    }

    [Fact]
    public void Analyze_flags_cyrillic_injection_mid_draft()
    {
        const string draft = """
            She opened the door and stepped into the rain.

            Он стоял у окна и смотрел на улицу, не двигаясь ни на шаг.
            """;

        var analysis = LanguageContextShiftDetector.Analyze(draft);

        Assert.True(analysis.HasShift);
        Assert.Equal("Latin", analysis.BaselineScript);
        Assert.Contains(analysis.Findings, f => f.ParagraphIndex == 1 && f.DetectedScript == "Cyrillic");
    }

    [Fact]
    public void MergeIntoCompliance_fails_and_adds_fix_instructions()
    {
        const string draft = """
            Rain hammered the glass.

            彼は窓辺に立って、動かずに通りを見下ろしていた。
            """;

        var analysis = LanguageContextShiftDetector.Analyze(draft);
        var verdict = LanguageContextShiftDetector.MergeIntoCompliance(
            new ComplianceVerdict { Pass = true },
            analysis);

        Assert.False(verdict.Pass);
        Assert.NotEmpty(verdict.Violations);
        Assert.NotEmpty(verdict.FixInstructions);
    }

    [Fact]
    public void MergeIntoQuality_lowers_score_on_shift()
    {
        const string draft = """
            Rain hammered the glass.

            彼は窓辺に立って、動かずに通りを見下ろしていた。
            """;

        var analysis = LanguageContextShiftDetector.Analyze(draft);
        var verdict = LanguageContextShiftDetector.MergeIntoQuality(
            new QualityVerdict { Score = 82 },
            analysis);

        Assert.True(verdict.Score <= 48);
        Assert.NotEmpty(verdict.Issues);
    }

    [Fact]
    public void Analyze_ignores_brief_accented_latin()
    {
        const string draft = """
            She ordered a café au lait and watched the résumé flutter off the table.

            He shrugged and said nothing.
            """;

        var analysis = LanguageContextShiftDetector.Analyze(draft);

        Assert.False(analysis.HasShift);
    }
}
