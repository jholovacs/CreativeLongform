using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

public class GenerationRun
{
    public Guid Id { get; set; }
    public Guid SceneId { get; set; }
    public Scene Scene { get; set; } = null!;
    public GenerationRunStatus Status { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
    public int MaxRepairIterations { get; set; } = 5;
    /// <summary>When true, pipeline stops after draft passes quality; user must call finalize for post-state.</summary>
    public bool StopAfterDraft { get; set; }
    /// <summary>Optional override for minimum draft word target (e.g. 1500).</summary>
    public int? MinWordsOverride { get; set; }
    public string? FinalDraftText { get; set; }
    public ICollection<StateSnapshot> StateSnapshots { get; set; } = new List<StateSnapshot>();
    public ICollection<LlmCall> LlmCalls { get; set; } = new List<LlmCall>();
    public ICollection<ComplianceEvaluation> ComplianceEvaluations { get; set; } = new List<ComplianceEvaluation>();
}
