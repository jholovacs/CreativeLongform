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
    public string? FinalDraftText { get; set; }
    public ICollection<StateSnapshot> StateSnapshots { get; set; } = new List<StateSnapshot>();
    public ICollection<LlmCall> LlmCalls { get; set; } = new List<LlmCall>();
    public ICollection<ComplianceEvaluation> ComplianceEvaluations { get; set; } = new List<ComplianceEvaluation>();
}
