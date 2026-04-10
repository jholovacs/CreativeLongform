using CreativeLongform.Domain.Entities;

namespace CreativeLongform.Application.Manuscript;

public static class ManuscriptAssembly
{
    /// <summary>Concatenate scene manuscripts in order with optional scene headings (markdown).</summary>
    public static string AssembleChapter(IEnumerable<Scene> scenesInOrder)
    {
        var parts = new List<string>();
        foreach (var s in scenesInOrder)
        {
            var body = string.IsNullOrWhiteSpace(s.ManuscriptText) ? null : s.ManuscriptText.Trim();
            if (body is null) continue;
            var title = string.IsNullOrWhiteSpace(s.Title) ? null : s.Title.Trim();
            parts.Add(title is null ? body : $"## {title}\n\n{body}");
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>Concatenate chapters in order; each chapter contains assembled scene text under a chapter heading.</summary>
    public static string AssembleBook(IEnumerable<Chapter> chaptersInOrder)
    {
        var parts = new List<string>();
        foreach (var ch in chaptersInOrder)
        {
            var scenes = ch.Scenes.OrderBy(s => s.Order).ToList();
            var inner = AssembleChapter(scenes);
            if (string.IsNullOrWhiteSpace(inner)) continue;
            var chTitle = string.IsNullOrWhiteSpace(ch.Title) ? "Chapter" : ch.Title.Trim();
            parts.Add($"# {chTitle}\n\n{inner}");
        }
        return string.Join("\n\n", parts);
    }
}
