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
        CancellationToken cancellationToken = default)
    {
        return _hub.Clients.Group(generationRunId.ToString("D")).SendAsync(
            eventName,
            new { runId = generationRunId, step, detail },
            cancellationToken);
    }
}
