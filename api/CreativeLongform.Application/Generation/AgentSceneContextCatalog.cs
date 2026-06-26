using System.Text;
using CreativeLongform.Domain.Entities;

namespace CreativeLongform.Application.Generation;

/// <summary>Timeline-ordered scene context for <c>query_timeline</c> (other scenes in the book).</summary>
public sealed class AgentSceneContextCatalog
{
    private const int ProseExcerptChars = 600;
    private readonly Guid _currentSceneId;
    private readonly IReadOnlyList<SceneTimelineRow> _rows;

    private AgentSceneContextCatalog(Guid currentSceneId, IReadOnlyList<SceneTimelineRow> rows)
    {
        _currentSceneId = currentSceneId;
        _rows = rows;
    }

    public static AgentSceneContextCatalog Create(Book book, Scene currentScene, IReadOnlyList<Scene> bookScenes)
    {
        var timelineSort = bookScenes
            .Select(s => new
            {
                Scene = s,
                TimelineKey = s.TimelineEntry?.SortKey,
                ChapterOrder = s.Chapter?.Order ?? 0,
                SceneOrder = s.Order
            })
            .OrderBy(x => x.TimelineKey ?? (decimal)(x.ChapterOrder * 1000 + x.SceneOrder))
            .ThenBy(x => x.ChapterOrder)
            .ThenBy(x => x.SceneOrder)
            .Select((x, i) => new SceneTimelineRow(
                x.Scene,
                i,
                x.Scene.Id == currentScene.Id,
                x.ChapterOrder,
                x.SceneOrder))
            .ToList();

        return new AgentSceneContextCatalog(currentScene.Id, timelineSort);
    }

    /// <summary>when: before | after | all | current</summary>
    public string Query(string? query, string? when)
    {
        var q = query?.Trim() ?? "";
        var w = (when ?? "all").Trim().ToLowerInvariant();
        var currentIndex = _rows.FirstOrDefault(r => r.IsCurrent)?.TimelineIndex ?? -1;

        IEnumerable<SceneTimelineRow> scope = w switch
        {
            "before" when currentIndex >= 0 => _rows.Where(r => r.TimelineIndex < currentIndex),
            "after" when currentIndex >= 0 => _rows.Where(r => r.TimelineIndex > currentIndex),
            "current" => _rows.Where(r => r.IsCurrent),
            _ => _rows
        };

        if (q.Length > 0)
        {
            scope = scope.Where(r => RowMatches(r, q));
        }

        var list = scope.ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Timeline scene query: \"{q}\" (when: {w}, {list.Count} row(s))");
        sb.AppendLine("(Story order uses timeline SortKey when set, else chapter/scene order.)");
        sb.AppendLine();

        if (list.Count == 0)
        {
            sb.AppendLine("(No matching scenes. Try when: before|after|all, or a character/place keyword.)");
            return sb.ToString().TrimEnd();
        }

        foreach (var row in list)
        {
            AppendRow(sb, row, currentIndex);
        }

        return sb.ToString().TrimEnd();
    }

    private static bool RowMatches(SceneTimelineRow row, string q)
    {
        var s = row.Scene;
        var hay = $"{s.Title} {s.Synopsis} {s.Instructions} {s.ManuscriptText} {s.LatestDraftText}";
        return hay.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendRow(StringBuilder sb, SceneTimelineRow row, int currentIndex)
    {
        var s = row.Scene;
        var tag = row.IsCurrent ? "CURRENT SCENE"
            : currentIndex < 0 ? "story order"
            : row.TimelineIndex < currentIndex ? "earlier in story"
            : "later in story";
        sb.AppendLine($"[scene] Ch{s.Chapter?.Order ?? row.ChapterOrder} ¶{s.Order}: {s.Title} ({tag})");
        sb.AppendLine($"  id: {s.Id:D}");
        if (!string.IsNullOrWhiteSpace(s.Synopsis))
            sb.AppendLine($"  synopsis: {Truncate(s.Synopsis.Trim(), 400)}");
        if (!string.IsNullOrWhiteSpace(s.Instructions))
            sb.AppendLine($"  instructions: {Truncate(s.Instructions.Trim(), 300)}");
        var prose = !string.IsNullOrWhiteSpace(s.ManuscriptText) ? s.ManuscriptText : s.LatestDraftText;
        if (!string.IsNullOrWhiteSpace(prose))
            sb.AppendLine($"  prose excerpt: {Truncate(prose.Trim(), ProseExcerptChars)}");
        sb.AppendLine();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private sealed record SceneTimelineRow(Scene Scene, int TimelineIndex, bool IsCurrent, int ChapterOrder, int SceneOrder);
}
