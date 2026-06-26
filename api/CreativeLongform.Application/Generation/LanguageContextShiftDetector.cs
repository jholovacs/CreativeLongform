using System.Globalization;
using System.Text;

namespace CreativeLongform.Application.Generation;

/// <summary>Detects unexpected mid-draft shifts to another writing system or language context.</summary>
public static class LanguageContextShiftDetector
{
    private const int BaselineSampleLetterBudget = 800;
    private const int MinForeignRunLetters = 4;
    private const double MinParagraphForeignLetterRatio = 0.14;
    private const int MaxExcerptChars = 48;

    public sealed record Finding(int ParagraphIndex, string Excerpt, string DetectedScript, string BaselineScript);

    public sealed record Analysis(bool HasShift, IReadOnlyList<Finding> Findings, string BaselineScript);

    public static Analysis Analyze(string? draft)
    {
        if (string.IsNullOrWhiteSpace(draft))
            return new Analysis(false, Array.Empty<Finding>(), "Latin");

        var paragraphs = SplitParagraphs(draft.Trim());
        if (paragraphs.Count == 0)
            return new Analysis(false, Array.Empty<Finding>(), "Latin");

        var baseline = InferBaselineScript(paragraphs);

        var findings = new List<Finding>();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var finding = AnalyzeParagraph(paragraphs[i], i, baseline);
            if (finding is not null)
                findings.Add(finding);
        }

        return new Analysis(findings.Count > 0, findings, baseline);
    }

    public static ComplianceVerdict MergeIntoCompliance(ComplianceVerdict verdict, Analysis analysis)
    {
        if (!analysis.HasShift)
            return verdict;

        verdict.Violations ??= new List<string>();
        verdict.FixInstructions ??= new List<string>();
        foreach (var f in analysis.Findings)
        {
            verdict.Violations.Add(
                $"Language context shift — {f.DetectedScript} text in a {f.BaselineScript}-language draft (¶{f.ParagraphIndex}): \"{f.Excerpt}\"");
            verdict.FixInstructions.Add(
                $"Rewrite ¶{f.ParagraphIndex} in the same language/script as the rest of the draft ({f.BaselineScript}); replace foreign excerpt \"{f.Excerpt}\" with equivalent prose.");
        }

        verdict.Pass = false;
        return verdict;
    }

    public static QualityVerdict MergeIntoQuality(QualityVerdict verdict, Analysis analysis)
    {
        if (!analysis.HasShift)
            return verdict;

        verdict.Issues ??= new List<string>();
        verdict.FixInstructions ??= new List<string>();
        foreach (var f in analysis.Findings)
        {
            verdict.Issues.Add(
                $"Unexpected {f.DetectedScript} language/script in ¶{f.ParagraphIndex}: \"{f.Excerpt}\"");
            verdict.FixInstructions.Add(
                $"Restore consistent language in ¶{f.ParagraphIndex} — rewrite \"{f.Excerpt}\" in {f.BaselineScript}.");
        }

        var current = verdict.Score ?? 70;
        verdict.Score = Math.Min(current, analysis.Findings.Count >= 2 ? 35 : 48);
        return verdict;
    }

    private static Finding? AnalyzeParagraph(string paragraph, int index, string baselineScript)
    {
        var letters = CollectLetters(paragraph);
        if (letters.Count < 12)
            return null;

        var foreignCount = letters.Count(l => l.Script != baselineScript && !IsIgnorableScript(l.Script));
        if (foreignCount >= MinForeignRunLetters &&
            (double)foreignCount / letters.Count >= MinParagraphForeignLetterRatio)
        {
            var dominantForeign = letters
                .Where(l => l.Script != baselineScript && !IsIgnorableScript(l.Script))
                .GroupBy(l => l.Script)
                .OrderByDescending(g => g.Count())
                .First()
                .Key;
            var excerpt = ExtractForeignExcerpt(paragraph, dominantForeign, baselineScript);
            return new Finding(index, excerpt, dominantForeign, baselineScript);
        }

        var run = FindForeignRun(paragraph, baselineScript);
        if (run is not null)
            return new Finding(index, run.Value.Excerpt, run.Value.Script, baselineScript);

        return null;
    }

    private static string InferBaselineScript(IReadOnlyList<string> paragraphs)
    {
        var opening = OpeningContextSample(paragraphs);
        var tallies = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (script, _) in EnumerateLetters(opening))
        {
            if (IsIgnorableScript(script))
                continue;
            tallies[script] = tallies.GetValueOrDefault(script) + 1;
        }

        if (tallies.Count == 0)
            return "Latin";

        return tallies.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>Opening paragraphs only — later text may already be a language shift.</summary>
    private static string OpeningContextSample(IReadOnlyList<string> paragraphs)
    {
        if (paragraphs.Count == 0)
            return "";

        var sample = paragraphs[0];
        if (CountScriptLetters(sample) < 25 && paragraphs.Count > 1)
            sample = paragraphs[0] + "\n\n" + paragraphs[1];

        return sample.Length <= BaselineSampleLetterBudget
            ? sample
            : sample[..BaselineSampleLetterBudget];
    }

    private static int CountScriptLetters(string text) =>
        EnumerateLetters(text).Count(l => !IsIgnorableScript(l.Script));

    private static (string Script, string Excerpt)? FindForeignRun(string paragraph, string baselineScript)
    {
        var runScript = "";
        var runStart = -1;
        var runLen = 0;
        for (var i = 0; i < paragraph.Length; i++)
        {
            if (!TryGetLetterScript(paragraph, i, out var script, out var charLen))
            {
                if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
                {
                    var excerpt = paragraph.Substring(runStart, Math.Min(MaxExcerptChars, runLen)).Trim();
                    return (runScript, excerpt.Length > 0 ? excerpt : paragraph[runStart..Math.Min(paragraph.Length, runStart + MaxExcerptChars)]);
                }

                runScript = "";
                runStart = -1;
                runLen = 0;
                continue;
            }

            if (script == baselineScript || IsIgnorableScript(script))
            {
                if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
                {
                    var excerpt = paragraph.Substring(runStart, Math.Min(MaxExcerptChars, runLen)).Trim();
                    return (runScript, excerpt);
                }

                runScript = "";
                runStart = -1;
                runLen = 0;
                continue;
            }

            if (runScript != script)
            {
                runScript = script;
                runStart = i;
                runLen = 1;
            }
            else
            {
                runLen++;
            }

            i += charLen - 1;
        }

        if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
        {
            var excerpt = paragraph.Substring(runStart, Math.Min(MaxExcerptChars, runLen)).Trim();
            return (runScript, excerpt);
        }

        return null;
    }

    private static string ExtractForeignExcerpt(string paragraph, string foreignScript, string baselineScript)
    {
        var run = FindForeignRun(paragraph, baselineScript);
        if (run is not null && string.Equals(run.Value.Script, foreignScript, StringComparison.OrdinalIgnoreCase))
            return TruncateExcerpt(run.Value.Excerpt);

        foreach (var (script, start, length) in EnumerateForeignSpans(paragraph, baselineScript))
        {
            if (string.Equals(script, foreignScript, StringComparison.OrdinalIgnoreCase))
                return TruncateExcerpt(paragraph.Substring(start, length));
        }

        return TruncateExcerpt(paragraph.Trim());
    }

    private static IEnumerable<(string Script, int Start, int Length)> EnumerateForeignSpans(string paragraph, string baselineScript)
    {
        var runScript = "";
        var runStart = -1;
        var runLen = 0;
        for (var i = 0; i < paragraph.Length; i++)
        {
            if (!TryGetLetterScript(paragraph, i, out var script, out var charLen))
            {
                if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
                    yield return (runScript, runStart, Math.Min(runLen, MaxExcerptChars));
                runScript = "";
                runStart = -1;
                runLen = 0;
                continue;
            }

            if (script == baselineScript || IsIgnorableScript(script))
            {
                if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
                    yield return (runScript, runStart, Math.Min(runLen, MaxExcerptChars));
                runScript = "";
                runStart = -1;
                runLen = 0;
                continue;
            }

            if (runScript != script)
            {
                runScript = script;
                runStart = i;
                runLen = 1;
            }
            else
            {
                runLen++;
            }

            i += charLen - 1;
        }

        if (runLen >= MinForeignRunLetters && runScript != baselineScript && !IsIgnorableScript(runScript))
            yield return (runScript, runStart, Math.Min(runLen, MaxExcerptChars));
    }

    private static List<(string Script, char Char)> CollectLetters(string paragraph)
    {
        var list = new List<(string Script, char Char)>();
        for (var i = 0; i < paragraph.Length; i++)
        {
            if (!TryGetLetterScript(paragraph, i, out var script, out var charLen))
                continue;
            if (IsIgnorableScript(script))
                continue;
            list.Add((script, paragraph[i]));
            i += charLen - 1;
        }

        return list;
    }

    private static IEnumerable<(string Script, char Char)> EnumerateLetters(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (!TryGetLetterScript(text, i, out var script, out var charLen))
                continue;
            yield return (script, text[i]);
            i += charLen - 1;
        }
    }

    private static bool TryGetLetterScript(string text, int index, out string script, out int charLen)
    {
        script = "";
        charLen = 1;
        if (index >= text.Length)
            return false;

        Rune rune;
        var c = text[index];
        if (char.IsSurrogate(c))
        {
            if (!Rune.TryGetRuneAt(text, index, out rune))
                return false;
            if (!Rune.IsLetter(rune))
                return false;
            charLen = rune.Utf16SequenceLength;
        }
        else
        {
            if (!char.IsLetter(c))
                return false;
            rune = new Rune(c);
        }

        script = ScriptFromCodePoint(rune.Value);
        return true;
    }

    private static string ScriptFromCodePoint(int cp) => cp switch
    {
        >= 0x0000 and <= 0x024F => "Latin",
        >= 0x1E00 and <= 0x1EFF => "Latin",
        >= 0x2C60 and <= 0x2C7F => "Latin",
        >= 0xA720 and <= 0xA7FF => "Latin",
        >= 0xAB30 and <= 0xAB6F => "Latin",
        >= 0x0400 and <= 0x04FF => "Cyrillic",
        >= 0x0500 and <= 0x052F => "Cyrillic",
        >= 0x2DE0 and <= 0x2DFF => "Cyrillic",
        >= 0xA640 and <= 0xA69F => "Cyrillic",
        >= 0x0370 and <= 0x03FF => "Greek",
        >= 0x1F00 and <= 0x1FFF => "Greek",
        >= 0x4E00 and <= 0x9FFF => "Han",
        >= 0x3400 and <= 0x4DBF => "Han",
        >= 0x3040 and <= 0x309F => "Hiragana",
        >= 0x30A0 and <= 0x30FF => "Katakana",
        >= 0xAC00 and <= 0xD7AF => "Hangul",
        >= 0x1100 and <= 0x11FF => "Hangul",
        >= 0x0600 and <= 0x06FF => "Arabic",
        >= 0x0750 and <= 0x077F => "Arabic",
        >= 0x08A0 and <= 0x08FF => "Arabic",
        >= 0x0590 and <= 0x05FF => "Hebrew",
        >= 0x0900 and <= 0x097F => "Devanagari",
        >= 0x0E00 and <= 0x0E7F => "Thai",
        _ => "Other"
    };

    private static bool IsIgnorableScript(string script) =>
        script is "Common" or "Inherited" or "Unknown";

    private static string TruncateExcerpt(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxExcerptChars ? flat : flat[..MaxExcerptChars] + "…";
    }

    private static List<string> SplitParagraphs(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 0)
            .ToList();
}
