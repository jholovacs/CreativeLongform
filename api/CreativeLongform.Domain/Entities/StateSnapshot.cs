using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Persisted narrative state JSON at a pipeline step for a <see cref="GenerationRun"/> (pre-state, post-state, etc.).
/// </summary>
public class StateSnapshot
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to generation run.</summary>
    public Guid GenerationRunId { get; set; }
    /// <summary>Navigation to parent run.</summary>
    public GenerationRun GenerationRun { get; set; } = null!;
    /// <summary>Which pipeline phase produced this snapshot.</summary>
    public PipelineStep Step { get; set; }
    /// <summary>Version of the JSON schema for <see cref="StateJson"/>.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>JSON: narrative state (see NarrativeState schema in Application).</summary>
    public string StateJson { get; set; } = "{}";
    /// <summary>When the snapshot was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
