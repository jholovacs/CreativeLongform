using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Root aggregate for a single long-form story: metadata, measurement defaults, chapters, timeline, world-building entries,
/// and optional full-book manuscript snapshot. Used by OData, scene workflow, world-building UI, and manuscript assembly.
/// </summary>
public class Book
{
    /// <summary>Primary key; referenced by chapters, timeline, world elements, and LLM audit rows.</summary>
    public Guid Id { get; set; }
    /// <summary>Human-readable title for lists, navigation, and prompts.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Story-level tone and genre, e.g. science fiction adventure.</summary>
    public string StoryToneAndStyle { get; set; } = string.Empty;
    /// <summary>Optional extra guidance for style, audience, or content boundaries.</summary>
    public string? ContentStyleNotes { get; set; }
    /// <summary>High-level story synopsis for prompts and world-building.</summary>
    public string? Synopsis { get; set; }
    /// <summary>Earth-like metric vs US customary vs fully custom; merged with <see cref="MeasurementSystemJson"/> for prompts.</summary>
    public MeasurementPreset MeasurementPreset { get; set; } = MeasurementPreset.EarthMetric;
    /// <summary>Optional overrides and custom units (jsonb). Schema: MeasurementSystemPayload in Application.</summary>
    public string? MeasurementSystemJson { get; set; }
    /// <summary>Server timestamp when the book row was created (seed or API).</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Optional assembled or edited full-book manuscript (published snapshot).</summary>
    public string? ManuscriptText { get; set; }
    /// <summary>Ordered narrative units containing scenes; cascade-deleted with the book.</summary>
    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
    /// <summary>Story-chronology beats (scenes + world events) for visualization and sorting.</summary>
    public ICollection<TimelineEntry> TimelineEntries { get; set; } = new List<TimelineEntry>();
    /// <summary>Canon/draft lore, geography, characters, etc., scoped to this book.</summary>
    public ICollection<WorldElement> WorldElements { get; set; } = new List<WorldElement>();
    /// <summary>Audit of LLM calls for world-building features not tied to a scene generation run.</summary>
    public ICollection<LlmCall> WorldLlmCalls { get; set; } = new List<LlmCall>();
}
