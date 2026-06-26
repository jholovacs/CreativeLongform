using CreativeLongform.Application.Abstractions;
using CreativeLongform.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CreativeLongform.Api.Services;

public sealed class SignalRGenerationProgressNotifier : IGenerationProgressNotifier
{
    private readonly IHubContext<GenerationHub> _hub;
    private readonly GenerationProgressReplayBuffer _replay;

    public SignalRGenerationProgressNotifier(IHubContext<GenerationHub> hub, GenerationProgressReplayBuffer replay)
    {
        _hub = hub;
        _replay = replay;
    }

    public Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default,
        long? elapsedMsSinceRunStart = null,
        long? stepDurationMs = null,
        Guid? llmCallId = null,
        string? workingDocumentText = null,
        int? documentRevision = null)
    {
        var payload = new
        {
            runId = generationRunId,
            step,
            detail,
            elapsedMs = elapsedMsSinceRunStart,
            stepDurationMs,
            llmCallId,
            workingDocumentText,
            documentRevision
        };
        if (!string.Equals(eventName, "LlmStreamChunk", StringComparison.Ordinal))
            _replay.Record(generationRunId, eventName, payload);
        if (string.Equals(eventName, "RunFinished", StringComparison.Ordinal))
            _replay.RemoveRun(generationRunId);

        return _hub.Clients.Group(generationRunId.ToString("D")).SendAsync(
            eventName,
            payload,
            cancellationToken);
    }
}
