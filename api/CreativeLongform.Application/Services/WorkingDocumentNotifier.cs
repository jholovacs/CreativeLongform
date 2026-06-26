using CreativeLongform.Application.Abstractions;

namespace CreativeLongform.Application.Services;

/// <summary>Pushes the single mutable working document to generation progress clients.</summary>
internal static class WorkingDocumentNotifier
{
    public static Task NotifyAsync(
        IGenerationProgressNotifier notifier,
        Guid runId,
        int revision,
        string documentText,
        string changeSummary,
        Func<long> elapsedMs,
        CancellationToken cancellationToken,
        long stepDurationMs = 0)
    {
        return notifier.NotifyAsync(
            runId,
            "WorkingDocumentUpdated",
            changeSummary,
            detail: null,
            cancellationToken,
            elapsedMs(),
            stepDurationMs,
            workingDocumentText: documentText,
            documentRevision: revision);
    }

    public static Task NotifyAgentStateAsync(
        AgentEditLoopState state,
        string changeSummary,
        long stepDurationMs = 0)
    {
        state.WorkingDocumentRevision++;
        var text = AgenticEditLoop.JoinParagraphs(state.Paragraphs);
        return NotifyAsync(
            state.Notifier,
            state.RunId,
            state.WorkingDocumentRevision,
            text,
            changeSummary,
            state.PipelineElapsedMs,
            state.CancellationToken,
            stepDurationMs);
    }
}
