using System.Text;

namespace CreativeLongform.Application.Agent;

/// <summary>Word-count analysis for agent planning and scene break-up.</summary>
public static class AgentWordBudget
{
    public sealed record Analysis(
        int CurrentWords,
        int MinWords,
        int MaxWords,
        int Deficit,
        int ParagraphCount,
        double WordsPerParagraph,
        bool NeedsBreakUp,
        int SuggestedBeatCount);

    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static Analysis Analyze(string draft, int minWords, int maxWords, int paragraphCount)
    {
        minWords = Math.Max(1, minWords);
        maxWords = Math.Max(minWords, maxWords);
        var current = CountWords(draft);
        var deficit = Math.Max(0, minWords - current);
        var paragraphs = Math.Max(1, paragraphCount);
        var wpp = current / (double)paragraphs;
        var needsBreakUp = deficit > Math.Max(150, (int)(minWords * 0.12));
        var suggestedBeats = deficit <= 0
            ? 0
            : Math.Clamp((int)Math.Ceiling(deficit / 350.0), 2, 8);
        return new Analysis(current, minWords, maxWords, deficit, paragraphs, wpp, needsBreakUp, suggestedBeats);
    }

    public static string FormatCheckResult(Analysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("check_word_budget result:");
        sb.AppendLine($"  currentWords: {a.CurrentWords}");
        sb.AppendLine($"  targetRange: {a.MinWords}–{a.MaxWords}");
        sb.AppendLine($"  deficitToMin: {a.Deficit}");
        sb.AppendLine($"  paragraphs: {a.ParagraphCount} (~{a.WordsPerParagraph:0} words/¶)");
        if (a.Deficit <= 0)
        {
            sb.AppendLine("  status: at or above minimum — polish with compliance/quality; break_up_scene not required.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine($"  status: SHORT — needs ~{a.Deficit} more words.");
        if (a.NeedsBreakUp)
        {
            sb.AppendLine($"  recommendation: use break_up_scene with {a.SuggestedBeatCount} beats (~300–450 words each).");
            sb.AppendLine("    1. Map beats to scene synopsis / check_scene_brief gaps.");
            sb.AppendLine("    2. read_section full draft (¶0..last).");
            sb.AppendLine("    3. break_up_scene — expand thin ¶s and insert_after for missing beats (processes high ¶ first).");
            sb.AppendLine("    4. check_word_budget again, then compliance/quality.");
        }
        else
        {
            sb.AppendLine("  recommendation: invoke_writer on thin ¶s or one break_up_scene with 2 beats.");
        }

        return sb.ToString().TrimEnd();
    }

    public static string BuildWriterBeatInstruction(string instruction, int? targetWords)
    {
        var core = instruction.Trim();
        if (targetWords is > 0)
            return $"Write approximately {targetWords.Value} words for this beat. {core} Use dramatized action, dialogue, and interiority — not summary.";
        return $"Expand with substantive prose. {core}";
    }
}
