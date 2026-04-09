using System.Text;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.WorldBuilding;

public static class WorldContextBuilder
{
    private const int MaxDetailChars = 1200;

    /// <summary>Formats story-level tone and linked world elements for LLM prompts.</summary>
    public static string Build(Book book, IReadOnlyList<WorldElement> elements)
    {
        var sb = new StringBuilder();
        var measurementBlock = MeasurementPromptFormatter.Format(book);
        if (!string.IsNullOrWhiteSpace(measurementBlock))
        {
            sb.AppendLine(measurementBlock);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(book.StoryToneAndStyle))
            sb.AppendLine($"Story tone and style: {book.StoryToneAndStyle}");
        if (!string.IsNullOrWhiteSpace(book.ContentStyleNotes))
            sb.AppendLine($"Content style notes: {book.ContentStyleNotes}");

        if (elements.Count == 0)
            return sb.ToString().TrimEnd();

        sb.AppendLine();
        sb.AppendLine("Linked world-building (respect; do not contradict established facts):");
        foreach (var group in elements.GroupBy(e => e.Kind).OrderBy(g => g.Key))
        {
            sb.AppendLine($"[{group.Key}]");
            foreach (var el in group.OrderBy(e => e.Title))
            {
                var status = el.Status == WorldElementStatus.Canon ? "canon" : "draft";
                sb.AppendLine($"- ({status}) {el.Title}: {el.Summary}");
                if (string.IsNullOrWhiteSpace(el.Detail))
                    continue;
                var detail = el.Detail.Length > MaxDetailChars
                    ? el.Detail[..MaxDetailChars] + "…"
                    : el.Detail;
                sb.AppendLine($"  Detail: {detail}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
