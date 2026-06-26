using System.Collections.Concurrent;

namespace CreativeLongform.Api.Services;

/// <summary>Recent generation progress events for SignalR catch-up when the client joins after the pipeline starts.</summary>
public sealed class GenerationProgressReplayBuffer
{
    private const int MaxEventsPerRun = 80;
    private readonly ConcurrentDictionary<Guid, List<ReplayEntry>> _runs = new();

    public void Record(Guid runId, string eventName, object payload)
    {
        var list = _runs.GetOrAdd(runId, _ => new List<ReplayEntry>());
        lock (list)
        {
            list.Add(new ReplayEntry(eventName, payload));
            if (list.Count > MaxEventsPerRun)
                list.RemoveRange(0, list.Count - MaxEventsPerRun);
        }
    }

    public IReadOnlyList<ReplayEntry> GetReplay(Guid runId)
    {
        if (!_runs.TryGetValue(runId, out var list))
            return Array.Empty<ReplayEntry>();
        lock (list)
            return list.ToList();
    }

    public void RemoveRun(Guid runId) => _runs.TryRemove(runId, out _);

    public sealed record ReplayEntry(string EventName, object Payload);
}
