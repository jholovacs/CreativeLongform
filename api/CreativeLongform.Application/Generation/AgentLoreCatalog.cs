using System.Text;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Generation;

/// <summary>Searchable book/scene lore for the agentic edit loop (<c>query_lore</c> tool).</summary>
public sealed class AgentLoreCatalog
{
    private readonly Book _book;
    private readonly IReadOnlyList<WorldElement> _sceneElements;
    private readonly HashSet<Guid> _sceneElementIds;
    private readonly IReadOnlyList<WorldElement> _bookElements;
    private readonly IReadOnlyList<WorldElementLink> _bookLinks;

    private AgentLoreCatalog(
        Book book,
        IReadOnlyList<WorldElement> sceneElements,
        IReadOnlyList<WorldElement> bookElements,
        IReadOnlyList<WorldElementLink> bookLinks)
    {
        _book = book;
        _sceneElements = sceneElements;
        _sceneElementIds = sceneElements.Select(e => e.Id).ToHashSet();
        _bookElements = bookElements;
        _bookLinks = bookLinks;
    }

    public static AgentLoreCatalog Create(
        Book book,
        IReadOnlyList<WorldElement> sceneElements,
        IReadOnlyList<WorldElementLink> sceneScopedLinks,
        IReadOnlyList<WorldElement> bookElements,
        IReadOnlyList<WorldElementLink> bookLinks) =>
        new(book, sceneElements, bookElements, bookLinks);

    /// <summary>Case-insensitive search. Scope: scene, book, relationships, all.</summary>
    public string Query(string? query, string? scope)
    {
        var q = query?.Trim() ?? "";
        if (q.Length == 0)
            return "Error: query_lore requires non-empty \"query\".";

        var s = (scope ?? "all").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        sb.AppendLine($"Lore query: \"{q}\" (scope: {s})");
        sb.AppendLine();

        var matchedAny = false;

        if (s is "all" or "book")
        {
            matchedAny |= AppendBookFields(sb, q);
        }

        if (s is "all" or "scene")
        {
            matchedAny |= AppendElements(sb, _sceneElements, q, sceneLinked: true);
        }

        if (s is "all" or "book")
        {
            var offScene = _bookElements.Where(e => !_sceneElementIds.Contains(e.Id)).ToList();
            matchedAny |= AppendElements(sb, offScene, q, sceneLinked: false);
        }

        if (s is "all" or "relationships")
        {
            matchedAny |= AppendRelationships(sb, q);
        }

        if (!matchedAny)
            sb.AppendLine("(No matches. Try a shorter keyword, another scope, or a character/place title.)");

        return sb.ToString().TrimEnd();
    }

    private bool AppendBookFields(StringBuilder sb, string q)
    {
        var matched = false;
        void Line(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !ContainsIgnoreCase(value, q))
                return;
            matched = true;
            sb.AppendLine($"[book] {label}: {value.Trim()}");
        }

        Line("Story tone and style", _book.StoryToneAndStyle);
        Line("Content style notes", _book.ContentStyleNotes);
        Line("Book synopsis", _book.Synopsis);
        if (matched)
            sb.AppendLine();
        return matched;
    }

    private static bool AppendElements(StringBuilder sb, IReadOnlyList<WorldElement> elements, string q, bool sceneLinked)
    {
        var matched = false;
        foreach (var el in elements.OrderBy(e => e.Kind).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
        {
            if (!ElementMatches(el, q))
                continue;
            matched = true;
            var linkTag = sceneLinked ? "scene-linked" : "book-only (not linked to this scene — reference carefully)";
            var status = el.Status == WorldElementStatus.Canon ? "canon" : "draft";
            sb.AppendLine($"[{el.Kind}] {el.Title} ({status}, {linkTag})");
            sb.AppendLine($"  id: {el.Id:D}");
            if (!string.IsNullOrWhiteSpace(el.Summary))
                sb.AppendLine($"  summary: {el.Summary.Trim()}");
            if (!string.IsNullOrWhiteSpace(el.Detail))
                sb.AppendLine($"  detail: {el.Detail.Trim()}");
            sb.AppendLine();
        }

        return matched;
    }

    private bool AppendRelationships(StringBuilder sb, string q)
    {
        var byId = _bookElements.ToDictionary(e => e.Id);
        var matched = false;
        foreach (var link in _bookLinks.OrderBy(l => l.RelationLabel, StringComparer.OrdinalIgnoreCase))
        {
            if (!byId.TryGetValue(link.FromWorldElementId, out var from) ||
                !byId.TryGetValue(link.ToWorldElementId, out var to))
                continue;

            var label = string.IsNullOrWhiteSpace(link.RelationLabel) ? "related_to" : link.RelationLabel.Trim();
            var hay = $"{from.Title} {label} {to.Title} {link.RelationDetail}";
            if (!ContainsIgnoreCase(hay, q))
                continue;

            matched = true;
            var inScene = _sceneElementIds.Contains(from.Id) && _sceneElementIds.Contains(to.Id);
            var scopeTag = inScene ? "scene-linked" : "book-only";
            sb.AppendLine($"[relationship] {from.Title} —{label}→ {to.Title} ({scopeTag})");
            if (!string.IsNullOrWhiteSpace(link.RelationDetail))
                sb.AppendLine($"  detail: {link.RelationDetail.Trim()}");
            sb.AppendLine();
        }

        return matched;
    }

    private static bool ElementMatches(WorldElement el, string q) =>
        ContainsIgnoreCase(el.Title, q)
        || ContainsIgnoreCase(el.Summary, q)
        || ContainsIgnoreCase(el.Detail, q)
        || ContainsIgnoreCase(el.Slug, q);

    private static bool ContainsIgnoreCase(string? hay, string needle) =>
        !string.IsNullOrEmpty(hay)
        && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
