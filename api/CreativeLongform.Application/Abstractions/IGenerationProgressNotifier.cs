namespace CreativeLongform.Application.Abstractions;

public interface IGenerationProgressNotifier
{
    /// <param name="elapsedMsSinceRunStart">Wall time since pipeline run began (optional).</param>
    /// <param name="stepDurationMs">Duration of this event (e.g. one LLM round-trip or agent turn).</param>
    /// <param name="llmResponsePreview">Full model output for UI (optional).</param>
    /// <param name="llmRequestPayload">Full request body sent to the model (e.g. serialized Ollama chat request) for UI drill-in (optional).</param>
    Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default,
        long? elapsedMsSinceRunStart = null,
        long? stepDurationMs = null,
        string? llmResponsePreview = null,
        string? llmRequestPayload = null);
}
