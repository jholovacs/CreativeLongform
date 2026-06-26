using System.Text;

namespace CreativeLongform.Application.Agent;

/// <summary>Before/after excerpts for agent verification after edits.</summary>
public static class AgentEditDiff
{
    private const int ExcerptChars = 280;

    public static string Format(string before, string after)
    {
        var sb = new StringBuilder();
        sb.AppendLine("edit_diff:");
        sb.AppendLine("  before:");
        sb.AppendLine(IndentExcerpt(before));
        sb.AppendLine("  after:");
        sb.AppendLine(IndentExcerpt(after));
        if (string.Equals(Normalize(before), Normalize(after), StringComparison.Ordinal))
            sb.AppendLine("  warning: replacement is identical to original span.");
        return sb.ToString().TrimEnd();
    }

    private static string IndentExcerpt(string text)
    {
        var excerpt = text.Length <= ExcerptChars ? text : text[..ExcerptChars].TrimEnd() + "…";
        return "    " + excerpt.Replace("\n", "\n    ", StringComparison.Ordinal);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}
