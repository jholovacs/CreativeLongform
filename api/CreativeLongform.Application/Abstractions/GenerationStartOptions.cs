namespace CreativeLongform.Application.Abstractions;

/// <summary>Options for <see cref="IGenerationOrchestrator.StartGenerationAsync"/>.</summary>
public sealed class GenerationStartOptions
{
    /// <summary>Stop after draft passes quality gates; user must call finalize for end-state and canon.</summary>
    public bool StopAfterDraft { get; set; }

    /// <summary>Minimum words for the draft (defaults to Ollama:DraftMinWords).</summary>
    public int? MinWordsOverride { get; set; }

    /// <summary>Optional upper bound for the target length band in draft prompts (defaults to a server heuristic from min words).</summary>
    public int? MaxWordsOverride { get; set; }

    /// <summary>When true, skips the LLM prose quality gate and repair loop (compliance still runs).</summary>
    public bool SkipQualityGate { get; set; }

    /// <summary>Overrides <c>Ollama:QualityAcceptMinScore</c> for this run (0–100).</summary>
    public double? QualityAcceptMinScore { get; set; }

    /// <summary>Overrides <c>Ollama:QualityReviewOnlyMinScore</c> for this run (0–100).</summary>
    public double? QualityReviewOnlyMinScore { get; set; }
}

public sealed record FinalizeGenerationResult(string StateTableJson);
