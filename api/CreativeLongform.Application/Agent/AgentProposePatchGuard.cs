using CreativeLongform.Application.Generation;

namespace CreativeLongform.Application.Agent;

/// <summary>Blocks orchestrator prose pasted via propose_patch when a delegate model should run instead.</summary>
public static class AgentProposePatchGuard
{
    public const int MaxWordsSingleParagraph = 45;
    public const int MaxWordsMultiParagraph = 25;

    public static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static string? Validate(AgentEditActionDto action)
    {
        var replacement = action.Replacement?.Trim() ?? "";
        if (string.IsNullOrEmpty(replacement))
            return null;

        if (action.ParagraphStart is not { } ps || action.ParagraphEnd is not { } pe)
            return null;

        var words = CountWords(replacement);
        var span = Math.Max(1, pe - ps + 1);
        if (span > 1 && words > MaxWordsMultiParagraph)
            return BuildRejectMessage(words, span);

        if (words > MaxWordsSingleParagraph)
            return BuildRejectMessage(words, span);

        return null;
    }

    private static string BuildRejectMessage(int words, int span) =>
        $"Error: propose_patch is for micro-edits only (≤{MaxWordsSingleParagraph} words; you sent ~{words} over {span} ¶). " +
        "Use invoke_writer (creative), invoke_editor (voice/format), or invoke_corrector (mechanics) — " +
        "do NOT paste replacement prose into propose_patch; the specialist model writes the passage.";
}
