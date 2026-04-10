using System.Text;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.WorldBuilding;

public static class WorldContextBuilder
{
    private const int MaxDetailChars = 1200;

    /// <summary>Formats story-level tone and linked world elements for LLM prompts.</summary>
    /// <param name="scopedLinks">
    /// Relationships to include only when both endpoints are attached to the scene; typically pre-filtered by the caller.
    /// </param>
    public static string Build(
        Book book,
        IReadOnlyList<WorldElement> elements,
        IReadOnlyList<WorldElementLink>? scopedLinks = null)
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
        if (!string.IsNullOrWhiteSpace(book.Synopsis))
            sb.AppendLine(
                $"Book synopsis (series tone and continuity — not a checklist of what this single scene must show on-page): {book.Synopsis}");

        if (elements.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("Linked world-building — no world elements are linked to this scene.");
            sb.AppendLine(
                "Only reference characters, places, and lore named in the scene synopsis/instructions above or in the state-before JSON; " +
                "do not import the broader cast from the book synopsis alone unless the scene text explicitly names them.");
            AppendInventionScopeFooter(sb);
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine();
        sb.AppendLine(
            "Linked world-building — only these world elements are linked to this scene (respect; do not contradict established facts):");
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

        if (scopedLinks is { Count: > 0 })
        {
            var byId = elements.ToDictionary(e => e.Id);
            var ordered = scopedLinks
                .OrderBy(l => l.RelationLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l.FromWorldElementId)
                .ThenBy(l => l.ToWorldElementId);
            sb.AppendLine();
            sb.AppendLine(
                "Relationships between scene-linked elements (only when both endpoints are attached to this scene):");
            foreach (var link in ordered)
            {
                if (!byId.TryGetValue(link.FromWorldElementId, out var from) ||
                    !byId.TryGetValue(link.ToWorldElementId, out var to))
                    continue;
                var label = string.IsNullOrWhiteSpace(link.RelationLabel) ? "related_to" : link.RelationLabel.Trim();
                sb.AppendLine($"- {from.Title} —{label}→ {to.Title}");
                if (string.IsNullOrWhiteSpace(link.RelationDetail))
                    continue;
                var rd = link.RelationDetail!.Trim();
                var rdOut = rd.Length > MaxDetailChars ? rd[..MaxDetailChars] + "…" : rd;
                sb.AppendLine($"  Detail: {rdOut}");
            }
        }

        AppendInventionScopeFooter(sb);
        return sb.ToString().TrimEnd();
    }

    private static void AppendInventionScopeFooter(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine(
            "Authoritative scope for this scene: Do not invent named characters, relationships between entities, or plot events " +
            "except as required by the scene synopsis/instructions above and the linked elements and relationship lines in this section. " +
            "Book-level synopsis and tone (elsewhere in this message) are not permission to add unrelated people, events, or lore.");
        sb.AppendLine(
            "Prose using this material should show, don't tell: dramatize through action, dialogue, and concrete detail rather than abstract exposition or emotional labeling.");
    }
}
