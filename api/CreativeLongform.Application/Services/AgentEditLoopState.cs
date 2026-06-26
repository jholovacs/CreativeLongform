using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Services;

/// <summary>Mutable session state for one agent edit run (tools + compliance tracking).</summary>
internal sealed class AgentEditLoopState
{
    public required List<string> Paragraphs { get; init; }
    public List<(int Start, int End)> ReadRanges { get; } = new();
    public HashSet<string> AppliedEditKeys { get; } = new(StringComparer.Ordinal);
    public int ComplianceCheckCount { get; set; }
    public string? LastComplianceDraftHash { get; set; }
    public ComplianceVerdict? LastComplianceVerdict { get; set; }
    public bool DraftEditedSinceLastCompliance { get; set; }
    public int QualityCheckCount { get; set; }
    public string? LastQualityDraftHash { get; set; }
    public QualityVerdict? LastQualityVerdict { get; set; }
    public bool DraftEditedSinceLastQuality { get; set; }
    public AgentEditRunOptions? RunOptions { get; init; }
    public ILogger Logger { get; init; } = null!;
    public IGenerationProgressNotifier Notifier { get; init; } = null!;
    public Guid RunId { get; init; }
    public Func<long> PipelineElapsedMs { get; init; } = null!;
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Last tool executed (lowercase action name) — drives “thinking about …” lines.</summary>
    public string? LastToolName { get; set; }

    /// <summary>Writer | Editor | Corrector after a delegated model responds.</summary>
    public string? LastDelegatedRole { get; set; }

    /// <summary>Specific subject for the next “Agent is thinking about …” line.</summary>
    public string? LastNarrativeHint { get; set; }

    /// <summary>Agent's last stated conclusion from its JSON reflection fields.</summary>
    public string? LastConclusion { get; set; }

    /// <summary>Agent's last stated next-step plan from its JSON reflection fields.</summary>
    public string? LastNextStep { get; set; }

    /// <summary>Recent tool requests and responses (oldest first) for agent context.</summary>
    public List<AgentToolHistoryEntry> ToolHistory { get; } = new();

    /// <summary>Monotonic revision counter for the working document (this agent session).</summary>
    public int WorkingDocumentRevision { get; set; }

    public void InvalidateCompliance()
    {
        LastComplianceVerdict = null;
        LastComplianceDraftHash = null;
    }

    public void InvalidateQuality()
    {
        LastQualityVerdict = null;
        LastQualityDraftHash = null;
    }

    public void MarkEdited()
    {
        InvalidateCompliance();
        InvalidateQuality();
        DraftEditedSinceLastCompliance = true;
        DraftEditedSinceLastQuality = true;
    }
}

internal sealed record AgentToolHistoryEntry(int Turn, string RequestSummary, string Result, bool IsError);

internal enum AgentToolExecuteStatus
{
    Ok,
    Error,
    Finished
}

internal readonly record struct AgentToolExecuteResult(AgentToolExecuteStatus Status, string Message);
