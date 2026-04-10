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
    /// <summary>Optional override for minimum draft word target (e.g. 1500). Expansion enforces at least this many words.</summary>
    public int? MinWordsOverride { get; set; }

    /// <summary>Optional upper bound for the draft length band in prompts (e.g. 2000). Defaults from min + server heuristics when null.</summary>
    public int? MaxWordsOverride { get; set; }
    /// <summary>When true, skips the critic quality loop (compliance still runs). Set via request or when Ollama:QualityGateEnabled is false.</summary>
    public bool SkipQualityGate { get; set; }
    /// <summary>Effective quality score threshold (0–100): at or above this, no automated repair. Snapshotted from request + Ollama config when the run starts.</summary>
    public double QualityAcceptMinScore { get; set; } = 75;
    /// <summary>Minimum score to pass the pipeline; between this and <see cref="QualityAcceptMinScore"/>, pass with annotations only. Snapshotted at run start.</summary>
    public double QualityReviewOnlyMinScore { get; set; } = 55;
    public string? FinalDraftText { get; set; }
    public ICollection<StateSnapshot> StateSnapshots { get; set; } = new List<StateSnapshot>();
    public ICollection<LlmCall> LlmCalls { get; set; } = new List<LlmCall>();
    public ICollection<ComplianceEvaluation> ComplianceEvaluations { get; set; } = new List<ComplianceEvaluation>();
}
