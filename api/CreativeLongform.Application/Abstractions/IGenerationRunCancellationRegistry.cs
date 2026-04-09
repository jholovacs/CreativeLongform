namespace CreativeLongform.Application.Abstractions;

/// <summary>Tracks <see cref="CancellationTokenSource"/> per generation run (singleton lifetime).</summary>
public interface IGenerationRunCancellationRegistry
{
    /// <summary>Registers a token source for a run; must pair with <see cref="RemoveRun"/>.</summary>
    CancellationTokenSource RegisterRun(Guid runId);

    void RemoveRun(Guid runId);

    /// <summary>Signals cancellation for the run if still registered.</summary>
    bool TryCancel(Guid runId);
}
