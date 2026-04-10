using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// A point on the book&apos;s story timeline: scene order in story-time and/or world events
/// that matter for progression (can link to world-building elements).
/// </summary>
public class TimelineEntry
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to owning book.</summary>
    public Guid BookId { get; set; }
    /// <summary>Navigation to book.</summary>
    public Book Book { get; set; } = null!;
    public TimelineEntryKind Kind { get; set; }

    /// <summary>
    /// Ordering along story chronology (not necessarily reading order). Lower = earlier in story-time.
    /// </summary>
    public decimal SortKey { get; set; }

    /// <summary>When <see cref="Kind"/> is <see cref="TimelineEntryKind.Scene"/>, the scene this row tracks.</summary>
    public Guid? SceneId { get; set; }
    public Scene? Scene { get; set; }

    /// <summary>
    /// Display label; for scenes, mirrors <see cref="Scene.Title"/> when created and can be left redundant.
    /// Required for <see cref="TimelineEntryKind.WorldEvent"/>.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional notes (e.g. what happens at this story beat).</summary>
    public string? Summary { get; set; }

    /// <summary>Optional link to a world element (e.g. <see cref="WorldElementKind.SignificantEvent"/>).</summary>
    public Guid? WorldElementId { get; set; }
    public WorldElement? WorldElement { get; set; }

    /// <summary>Base currency in an exchange pair relevant at this story-time beat (e.g. &quot;Imperial crown&quot;).</summary>
    public string? CurrencyPairBase { get; set; }

    /// <summary>Quote currency in the pair (e.g. &quot;River guild mark&quot;).</summary>
    public string? CurrencyPairQuote { get; set; }

    /// <summary>Issuer, nation, or organization whose regime defines this pair (central bank, empire treasury, etc.).</summary>
    public string? CurrencyPairAuthority { get; set; }

    /// <summary>Narrative or numeric exchange relationship at this point on the timeline.</summary>
    public string? CurrencyPairExchangeNote { get; set; }
}
