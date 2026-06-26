namespace CreativeLongform.Application.Options;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434/api";
    public string WriterModel { get; set; } = "llama3.2";
    public string CriticModel { get; set; } = "llama3.2";

    /// <summary>Grammar/punctuation correction when unset; null means use <see cref="CriticModel"/>.</summary>
    public string? CorrectionModel { get; set; }

    /// <summary>Default agentic-edit model when DB preference is unset; null means use <see cref="WriterModel"/>.</summary>
    public string? AgentModel { get; set; }

    /// <summary>Default world-building LLM when DB preference is unset; null means use <see cref="WriterModel"/>.</summary>
    public string? WorldBuildingModel { get; set; }

    /// <summary>Pre-state JSON when DB preference is unset; null means use <see cref="WriterModel"/>.</summary>
    public string? PreStateModel { get; set; }

    /// <summary>Post-state JSON when DB preference is unset; null means use <see cref="WriterModel"/>.</summary>
    public string? PostStateModel { get; set; }

    /// <summary>Light prose touch-ups when DB preference is unset; null means use <see cref="WriterModel"/>.</summary>
    public string? EditorModel { get; set; }

    /// <summary>
    /// Host path shared with the Ollama container for GGUF staging (URL import). Same path must be mounted in both services
    /// (e.g. /shared/import). Empty = URL import disabled.
    /// </summary>
    public string ImportStagingDirectory { get; set; } = "";

    /// <summary>
    /// Path on the API host used to report free/total disk space in the Ollama models UI (same filesystem as Ollama models when possible).
    /// Empty = use <see cref="ImportStagingDirectory"/> when set; if both empty, disk space is not shown.
    /// </summary>
    public string DiskSpaceCheckPath { get; set; } = "";

    /// <summary>Minimum word count for scene drafts (long-form fiction).</summary>
    public int DraftMinWords { get; set; } = 600;

    /// <summary>Ollama num_predict for draft and repair steps (raise if output truncates).</summary>
    public int DraftNumPredict { get; set; } = 8192;

    /// <summary>Ollama repeat_penalty for draft/repair prose (helps prevent token loops).</summary>
    public float DraftRepeatPenalty { get; set; } = 1.12f;

    /// <summary>Ollama repeat_last_n for draft/repair prose.</summary>
    public int DraftRepeatLastN { get; set; } = 256;

    /// <summary>If the first draft is shorter than DraftMinWords, run one expansion pass.</summary>
    public bool DraftExpandIfShort { get; set; } = true;

    /// <summary>Run orchestrating agent edit loop after the initial draft (lore lookup, compliance, writer delegation, targeted patches).</summary>
    public bool AgenticEditEnabled { get; set; } = true;

    /// <summary>Max LLM turns in the agentic edit loop (each turn is one tool JSON response).</summary>
    public int AgenticEditMaxTurns { get; set; } = 16;

    /// <summary>Ollama num_predict for each agentic edit JSON turn.</summary>
    public int AgenticEditNumPredict { get; set; } = 2048;

    /// <summary>Max compliance checks per agent edit session (each check is one critic LLM call).</summary>
    public int AgenticEditMaxComplianceChecks { get; set; } = 8;

    /// <summary>Max quality checks per agent edit session (Correct With LLM and similar).</summary>
    public int AgenticEditMaxQualityChecks { get; set; } = 8;

    /// <summary>Abort agent edit loop after this many consecutive tool/JSON failures.</summary>
    public int AgenticEditMaxConsecutiveFailures { get; set; } = 5;

    /// <summary>When terminal compliance or quality fails after the agent, run a remediation agent pass.</summary>
    public bool AgenticRepassEnabled { get; set; } = true;

    /// <summary>Prose quality critic when DB preference is unset; null means use <see cref="CriticModel"/>.</summary>
    public string? QualityCriticModel { get; set; }

    /// <summary>When false, the prose quality critic loop is skipped for all runs (compliance still runs). Per-run override: GenerationStartOptions.SkipQualityGate.</summary>
    public bool QualityGateEnabled { get; set; } = true;

    /// <summary>Quality score 0–100; at or above this, no automated repair is attempted.</summary>
    public double QualityAcceptMinScore { get; set; } = 75;

    /// <summary>
    /// Minimum score to pass the pipeline. Between this and <see cref="QualityAcceptMinScore"/>, the run passes with issues annotated for manual review (no repair).
    /// Below this, the repair loop runs until score reaches at least this or max attempts.
    /// </summary>
    public double QualityReviewOnlyMinScore { get; set; } = 55;
}
