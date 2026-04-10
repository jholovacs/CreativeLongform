using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Audit row for a single LLM request/response (pipeline step or world-building); exposed over OData for debugging and UI detail.
/// </summary>
public class LlmCall
{
    /// <summary>Primary key; referenced from SignalR progress payloads.</summary>
    public Guid Id { get; set; }
    public Guid? GenerationRunId { get; set; }
    public GenerationRun? GenerationRun { get; set; }
    /// <summary>When set, audit for world-building LLM calls not tied to a generation run.</summary>
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    /// <summary>Pipeline step name for scene runs; meaningful for world calls too.</summary>
    public PipelineStep Step { get; set; }
    /// <summary>Effective model name used for the call.</summary>
    public string Model { get; set; } = string.Empty;
    /// <summary>Serialized prompt / context.</summary>
    public string RequestJson { get; set; } = "{}";
    /// <summary>Raw model output text.</summary>
    public string ResponseText { get; set; } = string.Empty;
    /// <summary>When the call was persisted.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
