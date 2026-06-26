using CreativeLongform.Application.Agent;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Agent;

/// <summary>Session outcome metrics logged at agent loop end.</summary>
public static class AgentSessionMetrics
{
    public static void LogCompletion(
        Guid runId,
        AgentSessionKind? sessionKind,
        bool finishedCleanly,
        int turnsUsed,
        int maxTurns,
        int complianceChecks,
        int qualityChecks,
        int delegations,
        bool failureAbort,
        string draft,
        ILogger logger)
    {
        logger.LogInformation(
            "Agent session {RunId} kind={Kind} finished={Finished} turns={Turns}/{MaxTurns} complianceChecks={Compliance} qualityChecks={Quality} " +
            "delegations={Delegations} consecutiveFailureAbort={FailureAbort} draftWords={Words}",
            runId,
            sessionKind?.ToString() ?? "Unknown",
            finishedCleanly,
            turnsUsed,
            maxTurns,
            complianceChecks,
            qualityChecks,
            delegations,
            failureAbort,
            CountWords(draft));
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
