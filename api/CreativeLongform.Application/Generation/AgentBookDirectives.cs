using CreativeLongform.Domain.Entities;

namespace CreativeLongform.Application.Generation;

/// <summary>Book-level tone/style/synopsis block always shown to the agent orchestrator.</summary>
public static class AgentBookDirectives
{
    private const int SynopsisMaxChars = 1200;

    public static string Format(Book book)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BOOK DIRECTIVES (overarching — honor in every edit):");
        if (!string.IsNullOrWhiteSpace(book.StoryToneAndStyle))
            sb.AppendLine($"  Tone and style: {book.StoryToneAndStyle.Trim()}");
        if (!string.IsNullOrWhiteSpace(book.ContentStyleNotes))
            sb.AppendLine($"  Content style: {book.ContentStyleNotes.Trim()}");
        if (!string.IsNullOrWhiteSpace(book.Synopsis))
        {
            var syn = book.Synopsis.Trim();
            if (syn.Length > SynopsisMaxChars)
                syn = syn[..SynopsisMaxChars] + "…";
            sb.AppendLine($"  Book synopsis: {syn}");
        }

        if (sb.Length <= 40)
            sb.AppendLine("  (No book-level tone/style/synopsis stored — follow scene instructions.)");
        return sb.ToString().TrimEnd();
    }
}
