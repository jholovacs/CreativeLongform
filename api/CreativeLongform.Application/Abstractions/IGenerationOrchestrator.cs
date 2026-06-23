namespace CreativeLongform.Application.Abstractions;

public interface IGenerationOrchestrator
{
    Task<Guid> StartGenerationAsync(Guid sceneId, string? idempotencyKey, GenerationStartOptions? options,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation of a pending or running generation for the scene.</summary>
    Task<bool> CancelGenerationAsync(Guid sceneId, Guid generationRunId, CancellationToken cancellationToken = default);

    /// <summary>After a run ends in AwaitingUserReview, persist post-state and mark succeeded.</summary>
    Task<FinalizeGenerationResult> FinalizeGenerationAsync(Guid sceneId, Guid generationRunId, string? acceptedDraftText,
        string? approvedStateTableJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// LLM revision of draft while awaiting review.
    /// When <paramref name="selectionStart"/> and <paramref name="selectionEnd"/> are set (end exclusive, like a textarea),
    /// only that range is replaced; <paramref name="currentDraftText"/> should be the full editor text for context.
    /// </summary>
    Task<CorrectDraftResult> CorrectDraftAsync(Guid sceneId, Guid generationRunId, string userInstruction,
        string? currentDraftText = null, int? selectionStart = null, int? selectionEnd = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Derives beginning narrative state JSON for <paramref name="sceneId"/> via LLM, persists it on the scene, and returns it.
    /// When a previous scene exists, runs that scene's prose and beginning state through post-state inference, then pre-state for this scene.
    /// </summary>
    Task<DeriveBeginningStateResult> DeriveBeginningStateAsync(Guid sceneId,
        CancellationToken cancellationToken = default);

    /// <summary>Converts author plain-language beginning-state prose into schema JSON and persists both on the scene.</summary>
    Task<ConvertBeginningStateFromProseResult> ConvertBeginningStateFromProseAsync(Guid sceneId, string authorProse,
        CancellationToken cancellationToken = default);
}
