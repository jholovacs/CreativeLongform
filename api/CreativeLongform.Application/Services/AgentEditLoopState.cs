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

    public void InvalidateCompliance()
    {
        LastComplianceVerdict = null;
        LastComplianceDraftHash = null;
    }

    public void MarkEdited()
    {
        InvalidateCompliance();
        DraftEditedSinceLastCompliance = true;
    }
}

internal enum AgentToolExecuteStatus
{
    Ok,
    Error,
    Finished
}

internal readonly record struct AgentToolExecuteResult(AgentToolExecuteStatus Status, string Message);
