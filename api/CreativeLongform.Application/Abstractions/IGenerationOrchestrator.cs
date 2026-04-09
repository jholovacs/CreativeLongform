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

    /// <summary>LLM revision of draft while awaiting review.</summary>
    Task CorrectDraftAsync(Guid sceneId, Guid generationRunId, string userInstruction,
        CancellationToken cancellationToken = default);
}
