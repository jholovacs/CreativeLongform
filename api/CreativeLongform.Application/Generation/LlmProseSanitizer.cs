using System.Text;

namespace CreativeLongform.Application.Generation;

/// <summary>Separates model reasoning blocks from prose destined for drafts and manuscripts.</summary>
public static class LlmProseSanitizer
{
    public sealed record SplitResult(string Prose, string? ThinkingNotes);

    /// <summary>Removes thinking XML blocks from prose and returns their inner text as notes.</summary>
    public static SplitResult SplitThinkingFromProse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new SplitResult(string.Empty, null);

        var thinkingParts = new List<string>();
        var prose = raw;
        prose = ExtractBlocks(prose, "redacted_thinking", thinkingParts);
        prose = ExtractBlocks(prose, "thinking", thinkingParts);
        prose = CollapseExtraBlankLines(prose.Trim());

        if (thinkingParts.Count == 0)
            return new SplitResult(prose, null);

        var notes = string.Join("\n\n---\n\n", thinkingParts).Trim();
        return new SplitResult(prose, string.IsNullOrWhiteSpace(notes) ? null : notes);
    }

    private static string ExtractBlocks(string text, string tag, List<string> thinkingParts)
    {
        var open = $"<{tag}";
        var close = $"</{tag}>";
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var start = text.IndexOf(open, i, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                sb.Append(text.AsSpan(i));
                break;
            }

            sb.Append(text.AsSpan(i, start - i));
            var openEnd = text.IndexOf('>', start);
            if (openEnd < 0)
            {
                sb.Append(text.AsSpan(start));
                break;
            }

            var closeStart = text.IndexOf(close, openEnd + 1, StringComparison.OrdinalIgnoreCase);
            if (closeStart < 0)
            {
                sb.Append(text.AsSpan(start));
                break;
            }

            var inner = text[(openEnd + 1)..closeStart].Trim();
            if (!string.IsNullOrWhiteSpace(inner))
                thinkingParts.Add(inner);

            i = closeStart + close.Length;
        }

        return sb.ToString();
    }

    private static string CollapseExtraBlankLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var sb = new StringBuilder(text.Length);
        var blankRun = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;
                if (blankRun <= 2)
                    sb.Append('\n');
                continue;
            }

            blankRun = 0;
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(line.TrimEnd());
        }

        return sb.ToString().Trim();
    }
}
