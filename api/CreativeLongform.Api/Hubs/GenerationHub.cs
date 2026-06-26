using CreativeLongform.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace CreativeLongform.Api.Hubs;

public sealed class GenerationHub : Hub
{
    private readonly GenerationProgressReplayBuffer _replay;

    public GenerationHub(GenerationProgressReplayBuffer replay)
    {
        _replay = replay;
    }

    public async Task JoinRun(Guid runId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString("D"));
        foreach (var entry in _replay.GetReplay(runId))
            await Clients.Caller.SendAsync(entry.EventName, entry.Payload);
    }

    public Task LeaveRun(Guid runId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString("D"));
    }
}
