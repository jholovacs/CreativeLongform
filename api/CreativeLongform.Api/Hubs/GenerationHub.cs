using Microsoft.AspNetCore.SignalR;

namespace CreativeLongform.Api.Hubs;

public sealed class GenerationHub : Hub
{
    public Task JoinRun(Guid runId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString("D"));
    }

    public Task LeaveRun(Guid runId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString("D"));
    }
}
