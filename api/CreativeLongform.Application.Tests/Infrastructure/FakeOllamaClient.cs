using CreativeLongform.Application.Abstractions;

namespace CreativeLongform.Application.Tests.Infrastructure;

/// <summary>Queues canned chat responses for orchestrator and service unit tests.</summary>
public sealed class FakeOllamaClient : IOllamaClient
{
    private readonly Queue<string> _messageTexts = new();
    private readonly List<(string Model, bool JsonFormat)> _calls = new();
    private TaskCompletionSource? _pauseGate;

    public IReadOnlyList<(string Model, bool JsonFormat)> Calls => _calls;

    /// <summary>Blocks the next <see cref="ChatAsync"/> until <see cref="ReleasePause"/> is called.</summary>
    public void PauseBeforeNextChat() => _pauseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleasePause()
    {
        if (_pauseGate is null)
            return;
        _pauseGate.TrySetResult();
        _pauseGate = null;
    }

    public void Enqueue(string messageText) => _messageTexts.Enqueue(messageText);

    public void EnqueueEmptyJson(int count)
    {
        for (var i = 0; i < count; i++)
            Enqueue("{}");
    }

    public async Task<OllamaChatResult> ChatAsync(
        string model,
        IReadOnlyList<OllamaChatMessage> messages,
        bool jsonFormat,
        OllamaChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_pauseGate is not null)
            await _pauseGate.Task.WaitAsync(cancellationToken);

        _calls.Add((model, jsonFormat));
        if (_messageTexts.Count == 0)
            throw new InvalidOperationException("FakeOllamaClient: no response queued.");

        var text = _messageTexts.Dequeue();
        return new OllamaChatResult(model, text);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
