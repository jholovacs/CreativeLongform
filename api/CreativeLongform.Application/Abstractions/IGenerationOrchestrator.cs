namespace CreativeLongform.Application.Abstractions;

public interface IGenerationOrchestrator
{
    Task<Guid> StartGenerationAsync(Guid sceneId, string? idempotencyKey, CancellationToken cancellationToken = default);
}
