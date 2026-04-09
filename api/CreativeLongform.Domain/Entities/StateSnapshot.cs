using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

public class StateSnapshot
{
    public Guid Id { get; set; }
    public Guid GenerationRunId { get; set; }
    public GenerationRun GenerationRun { get; set; } = null!;
    public PipelineStep Step { get; set; }
    public int SchemaVersion { get; set; } = 1;
    /// <summary>JSON: narrative state (see NarrativeState schema in Application).</summary>
    public string StateJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
