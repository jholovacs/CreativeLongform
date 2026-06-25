using CreativeLongform.Application.Abstractions;

namespace CreativeLongform.Application.Tests.Infrastructure;

internal sealed class NoOpGenerationProgressNotifier : IGenerationProgressNotifier
{
    public Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default,
        long? elapsedMsSinceRunStart = null,
        long? stepDurationMs = null,
        Guid? llmCallId = null)
        => Task.CompletedTask;
}
