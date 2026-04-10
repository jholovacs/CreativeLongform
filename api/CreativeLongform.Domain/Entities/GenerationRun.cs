using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>
/// One execution of the scene prose pipeline for a <see cref="Scene"/>: draft, quality/compliance, optional repair loop,
/// finalize, and cancellation. Holds thresholds and snapshot of draft text; linked to <see cref="LlmCall"/> and <see cref="StateSnapshot"/> rows.
/// After the user finalizes the draft into <see cref="Scene.ManuscriptText"/>, all runs for that scene are deleted (cascade removes audit rows) to avoid unbounded log growth.
/// </summary>
public class GenerationRun
{
    /// <summary>Primary key; referenced by SignalR hub and OData filters for &quot;awaiting review&quot;.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to the scene being generated.</summary>
    public Guid SceneId { get; set; }
    /// <summary>Navigation to scene.</summary>
    public Scene Scene { get; set; } = null!;
    /// <summary>Lifecycle: running, succeeded, failed, cancelled, awaiting user review, etc.</summary>
    public GenerationRunStatus Status { get; set; }
    /// <summary>Optional client key to dedupe duplicate start requests.</summary>
    public string? IdempotencyKey { get; set; }
    /// <summary>When the run started (wall clock).</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>When the run finished or was cancelled.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>User-facing error when status is failed.</summary>
    public string? FailureReason { get; set; }
    /// <summary>Cap on automated repair rounds in the agentic loop.</summary>
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
    /// <summary>Latest draft text for this run (e.g. after repair); scene may mirror via workflow.</summary>
    public string? FinalDraftText { get; set; }
    /// <summary>Pre/post narrative state JSON snapshots for audit and finalize.</summary>
    public ICollection<StateSnapshot> StateSnapshots { get; set; } = new List<StateSnapshot>();
    /// <summary>Prompt/response audit for each pipeline step.</summary>
    public ICollection<LlmCall> LlmCalls { get; set; } = new List<LlmCall>();
    /// <summary>Structured compliance/quality verdicts per attempt.</summary>
    public ICollection<ComplianceEvaluation> ComplianceEvaluations { get; set; } = new List<ComplianceEvaluation>();
}
