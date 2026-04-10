namespace CreativeLongform.Application.Abstractions;

public interface IGenerationProgressNotifier
{
    /// <param name="elapsedMsSinceRunStart">Wall time since pipeline run began (optional).</param>
    /// <param name="stepDurationMs">Duration of this event (e.g. one LLM round-trip or agent turn).</param>
    /// <param name="llmCallId">When set, full request/response are stored in <c>LlmCalls</c>; clients load via OData.</param>
    Task NotifyAsync(
        Guid generationRunId,
        string eventName,
        string? step,
        string? detail,
        CancellationToken cancellationToken = default,
        long? elapsedMsSinceRunStart = null,
        long? stepDurationMs = null,
        Guid? llmCallId = null);
}
