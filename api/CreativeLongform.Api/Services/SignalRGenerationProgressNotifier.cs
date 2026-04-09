using CreativeLongform.Application.Abstractions;
using CreativeLongform.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace CreativeLongform.Api.Services;

public sealed class SignalRGenerationProgressNotifier : IGenerationProgressNotifier
{
    private readonly IHubContext<GenerationHub> _hub;

    public SignalRGenerationProgressNotifier(IHubContext<GenerationHub> hub)
    {
        _hub = hub;
    }

    public Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default,
        long? elapsedMsSinceRunStart = null,
        long? stepDurationMs = null,
        string? llmResponsePreview = null,
        string? llmRequestPayload = null)
    {
        return _hub.Clients.Group(generationRunId.ToString("D")).SendAsync(
            eventName,
            new
            {
                runId = generationRunId,
                step,
                detail,
                elapsedMs = elapsedMsSinceRunStart,
                stepDurationMs,
                llmPreview = llmResponsePreview,
                llmRequest = llmRequestPayload
            },
            cancellationToken);
    }
}
