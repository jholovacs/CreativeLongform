namespace CreativeLongform.Application.Abstractions;

public interface IWorldBuildingService
{
    Task<WorldBuildingApplyResult> ExtractFromTextAsync(Guid bookId, string text, CancellationToken cancellationToken = default);
    Task<WorldBuildingApplyResult> GenerateFromPromptAsync(Guid bookId, string prompt, CancellationToken cancellationToken = default);
}

public sealed class WorldBuildingApplyResult
{
    public IReadOnlyList<Guid> CreatedElementIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> CreatedLinkIds { get; init; } = Array.Empty<Guid>();
}
