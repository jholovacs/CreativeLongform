using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

public class Book
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Story-level tone and genre, e.g. science fiction adventure.</summary>
    public string StoryToneAndStyle { get; set; } = string.Empty;
    /// <summary>Optional extra guidance for style, audience, or content boundaries.</summary>
    public string? ContentStyleNotes { get; set; }
    /// <summary>Earth-like metric vs US customary vs fully custom; merged with <see cref="MeasurementSystemJson"/> for prompts.</summary>
    public MeasurementPreset MeasurementPreset { get; set; } = MeasurementPreset.EarthMetric;
    /// <summary>Optional overrides and custom units (jsonb). Schema: MeasurementSystemPayload in Application.</summary>
    public string? MeasurementSystemJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
    public ICollection<WorldElement> WorldElements { get; set; } = new List<WorldElement>();
    public ICollection<LlmCall> WorldLlmCalls { get; set; } = new List<LlmCall>();
}
