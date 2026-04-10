using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// A lore, geography, character, or other world-building record tied to a <see cref="Book"/>; can be linked to scenes,
/// other elements, and timeline rows for prompts and canon review.
/// </summary>
public class WorldElement
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to owning book.</summary>
    public Guid BookId { get; set; }
    /// <summary>Navigation to book.</summary>
    public Book Book { get; set; } = null!;
    /// <summary>Category of entry (lore, geography, character, etc.).</summary>
    public WorldElementKind Kind { get; set; }
    /// <summary>Short display name.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Optional stable slug for URLs or keys.</summary>
    public string? Slug { get; set; }
    /// <summary>Brief text for lists and LLM context.</summary>
    public string Summary { get; set; } = string.Empty;
    /// <summary>Longer canon text for prompts (may be truncated in context builders).</summary>
    public string Detail { get; set; } = string.Empty;
    /// <summary>Draft vs canon workflow state.</summary>
    public WorldElementStatus Status { get; set; }
    /// <summary>How the entry was created (manual, import, LLM, etc.).</summary>
    public WorldElementProvenance Provenance { get; set; }
    /// <summary>Optional extensible JSON for future fields.</summary>
    public string? MetadataJson { get; set; }
    /// <summary>Created timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Scenes that reference this element for generation prompts.</summary>
    public ICollection<SceneWorldElement> SceneWorldElements { get; set; } = new List<SceneWorldElement>();
    /// <summary>Directed edges where this element is the source.</summary>
    public ICollection<WorldElementLink> OutgoingLinks { get; set; } = new List<WorldElementLink>();
    /// <summary>Directed edges where this element is the target.</summary>
    public ICollection<WorldElementLink> IncomingLinks { get; set; } = new List<WorldElementLink>();
    /// <summary>Timeline beats that optionally link to this element.</summary>
    public ICollection<TimelineEntry> TimelineEntries { get; set; } = new List<TimelineEntry>();
}
