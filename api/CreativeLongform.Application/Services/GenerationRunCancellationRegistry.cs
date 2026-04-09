using System.Collections.Concurrent;
using CreativeLongform.Application.Abstractions;

namespace CreativeLongform.Application.Services;

public sealed class GenerationRunCancellationRegistry : IGenerationRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cts = new();

    public CancellationTokenSource RegisterRun(Guid runId)
    {
        var cts = new CancellationTokenSource();
        if (!_cts.TryAdd(runId, cts))
        {
            cts.Dispose();
            throw new InvalidOperationException($"Run {runId} is already registered.");
        }

        return cts;
    }

    public void RemoveRun(Guid runId)
    {
        if (_cts.TryRemove(runId, out var cts))
            cts.Dispose();
    }

    public bool TryCancel(Guid runId)
    {
        if (!_cts.TryGetValue(runId, out var cts))
            return false;
        cts.Cancel();
        return true;
    }
}
