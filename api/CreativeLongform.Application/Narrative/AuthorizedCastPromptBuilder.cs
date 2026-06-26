using System.Text;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Narrative;

/// <summary>Explicit authorized cast list for compliance and agent prompts.</summary>
public static class AuthorizedCastPromptBuilder
{
    public static string Build(
        string? stateBeforeJson,
        IReadOnlyList<WorldElement>? linkedElements,
        string? sceneInstructions = null)
    {
        var entries = new List<(string Name, string Source)>();

        if (LlmJson.TryNormalizeStateJson(stateBeforeJson, out var normalized))
        {
            var state = LlmJson.Deserialize<NarrativeState>(normalized);
            if (state?.Characters is { Count: > 0 })
            {
                foreach (var c in state.Characters)
                {
                    var name = c.Name?.Trim();
                    if (!string.IsNullOrEmpty(name))
                        AddUnique(entries, name, "stateBefore (scene entry)");
                }
            }
        }

        if (linkedElements is { Count: > 0 })
        {
            foreach (var el in linkedElements.Where(e => e.Kind == WorldElementKind.Character))
            {
                var title = el.Title?.Trim();
                if (!string.IsNullOrEmpty(title))
                    AddUnique(entries, title, "linked world-building (Character)");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(
            "AUTHORIZED CAST (permitted in this scene — do NOT flag these as invented or unauthorized when used appropriately):");
        if (entries.Count == 0)
            sb.AppendLine("(none parsed from stateBefore.characters[] or linked Character elements)");
        else
        {
            foreach (var (name, source) in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                sb.AppendLine($"- {name} ({source})");
        }

        sb.AppendLine(
            "Also authorized: any person explicitly named in the scene synopsis/instructions below, even if absent from stateBefore or this list.");
        sb.AppendLine(
            "stateBefore is the entry snapshot only — characters may enter during the scene. On-page introduction in the draft establishes characters for later references in the same draft.");

        if (!string.IsNullOrWhiteSpace(sceneInstructions))
        {
            sb.AppendLine();
            sb.AppendLine(
                "(Scene instructions above may name additional cast — treat those names as authorized even when not repeated here.)");
        }

        return sb.ToString().TrimEnd();
    }

    private static void AddUnique(List<(string Name, string Source)> entries, string name, string source)
    {
        if (entries.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        entries.Add((name, source));
    }
}
