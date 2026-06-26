using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Narrative;
using CreativeLongform.Application.Options;

namespace CreativeLongform.Application.Agent;

/// <summary>Delegates wired by the host (orchestrator) into an agent session.</summary>
public sealed class AgentSessionDelegates
{
    public Func<string, CancellationToken, Task<ComplianceVerdict>>? RunComplianceAsync { get; init; }
    public Func<string, CancellationToken, Task<QualityVerdict>>? RunQualityAsync { get; init; }
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeWriterAsync { get; init; }
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeCorrectorAsync { get; init; }
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeEditorAsync { get; init; }
}

/// <summary>Inputs for building a standardized <see cref="AgentEditRunOptions"/>.</summary>
public sealed class AgentSessionBuildRequest
{
    public required AgentSessionKind Kind { get; init; }
    public required OllamaOptions OllamaOptions { get; init; }
    public required AgentSessionDelegates Delegates { get; init; }
    public required string StateBeforeJson { get; init; }
    public required string AuthorizedCastBlock { get; init; }
    public required AgentBookContext BookContext { get; init; }
    public required string BookDirectiveBlock { get; init; }
    public required string SceneInstructionsBlock { get; init; }
    public string? NarrativePerspective { get; init; }
    public string? NarrativeTense { get; init; }
    public string? ExpectedEndNotes { get; init; }
    public int ParagraphCount { get; init; }
    public int InitialWorkingDocumentRevision { get; init; }
    public bool SkipQualityGate { get; init; }
    public double? QualityReviewMinScore { get; init; }
    public double? QualityAcceptMinScore { get; init; }
    public string? UserCorrectionMission { get; init; }
    public int? SelectionStart { get; init; }
    public int? SelectionEnd { get; init; }
    public int MinWordsTarget { get; init; }
    public int MaxWordsTarget { get; init; }
}

/// <summary>Single place to configure agent capabilities — used by pipeline and Correct With LLM.</summary>
public static class AgentSessionFactory
{
    public static AgentEditRunOptions Build(AgentSessionBuildRequest request)
    {
        var opts = request.OllamaOptions;
        var qualityInLoop = opts.QualityGateEnabled && !request.SkipQualityGate;
        var paragraphs = Math.Max(1, request.ParagraphCount);

        return new AgentEditRunOptions
        {
            SessionKind = request.Kind,
            StateBeforeJson = request.StateBeforeJson,
            ContinuityBriefBlock = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(request.StateBeforeJson),
            NarrativePerspective = request.NarrativePerspective,
            NarrativeTense = request.NarrativeTense,
            ExpectedEndNotes = request.ExpectedEndNotes,
            SceneInstructionsBlock = request.SceneInstructionsBlock,
            AuthorizedCastBlock = request.AuthorizedCastBlock,
            Lore = request.BookContext.Lore,
            Timeline = request.BookContext.Timeline,
            BookDirectiveBlock = request.BookDirectiveBlock,
            InitialWorkingDocumentRevision = request.InitialWorkingDocumentRevision,
            UserCorrectionMission = request.UserCorrectionMission,
            SelectionStart = request.SelectionStart,
            SelectionEnd = request.SelectionEnd,
            MaxComplianceChecks = AgentSessionBudget.ScaleChecks(Math.Max(1, opts.AgenticEditMaxComplianceChecks), paragraphs),
            MaxQualityChecks = AgentSessionBudget.ScaleChecks(Math.Max(1, opts.AgenticEditMaxQualityChecks), paragraphs),
            MaxConsecutiveToolFailures = Math.Max(1, opts.AgenticEditMaxConsecutiveFailures),
            QualityReviewMinScore = request.QualityReviewMinScore,
            QualityAcceptMinScore = request.QualityAcceptMinScore,
            MinWordsTarget = Math.Max(1, request.MinWordsTarget),
            MaxWordsTarget = Math.Max(request.MinWordsTarget, request.MaxWordsTarget),
            RequireQualityBeforeFinish = qualityInLoop,
            RunComplianceAsync = request.Delegates.RunComplianceAsync,
            RunQualityAsync = qualityInLoop ? request.Delegates.RunQualityAsync : null,
            InvokeWriterAsync = request.Delegates.InvokeWriterAsync,
            InvokeCorrectorAsync = request.Delegates.InvokeCorrectorAsync,
            InvokeEditorAsync = request.Delegates.InvokeEditorAsync
        };
    }

    public static int ComputeMaxTurns(OllamaOptions opts, int paragraphCount) =>
        AgentSessionBudget.ScaleTurns(Math.Max(1, opts.AgenticEditMaxTurns), paragraphCount);
}
