namespace CreativeLongform.Application.Abstractions;

public interface IGenerationProgressNotifier
{
    Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default);
}
