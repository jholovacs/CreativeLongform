namespace CreativeLongform.Application.Abstractions;

/// <summary>Options for <see cref="IGenerationOrchestrator.StartGenerationAsync"/>.</summary>
public sealed class GenerationStartOptions
{
    /// <summary>Stop after draft passes quality gates; user must call finalize for end-state and canon.</summary>
    public bool StopAfterDraft { get; set; }

    /// <summary>Minimum words for the draft (defaults to Ollama:DraftMinWords, typically 1500–2000).</summary>
    public int? MinWordsOverride { get; set; }
}

public sealed record FinalizeGenerationResult(string StateTableJson);
