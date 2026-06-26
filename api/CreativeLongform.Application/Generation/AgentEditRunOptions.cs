namespace CreativeLongform.Application.Generation;

/// <summary>Optional capabilities for <see cref="Services.AgenticEditLoop"/> (lore lookup, compliance, writer delegation).</summary>
public sealed class AgentEditRunOptions
{
    /// <summary>Beginning narrative state JSON for compliance and writer calls.</summary>
    public string StateBeforeJson { get; init; } = "{}";

    public AgentLoreCatalog? Lore { get; init; }

    /// <summary>Runs instruction compliance on the supplied full draft text; returns verdict for the agent.</summary>
    public Func<string, CancellationToken, Task<ComplianceVerdict>>? RunComplianceAsync { get; init; }

    /// <summary>Asks the prose writer model to rewrite a paragraph span; returns replacement prose only.</summary>
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeWriterAsync { get; init; }

    /// <summary>Asks the critic/correction model to fix grammar and punctuation in a paragraph span; returns replacement prose only.</summary>
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeCorrectorAsync { get; init; }

    /// <summary>Asks the editor model for light touch-ups (tense, perspective, formatting); returns replacement prose only.</summary>
    public Func<AgentWriterInvokeRequest, CancellationToken, Task<string>>? InvokeEditorAsync { get; init; }

    /// <summary>Max compliance checks per agent session (prevents critic spam).</summary>
    public int MaxComplianceChecks { get; init; } = 8;

    /// <summary>Abort agent loop after this many consecutive tool errors (configurable).</summary>
    public int MaxConsecutiveToolFailures { get; init; } = AgentToolRegistry.DefaultMaxConsecutiveFailures;

    /// <summary>Always-visible book tone/style/synopsis block.</summary>
    public string BookDirectiveBlock { get; init; } = "";

    public AgentSceneContextCatalog? Timeline { get; init; }
}

public sealed class AgentWriterInvokeRequest
{
    public int ParagraphStart { get; init; }
    public int ParagraphEnd { get; init; }
    public string Instruction { get; init; } = "";
    /// <summary>Violations/fixInstructions from compliance (auto-filled when the agent omits them).</summary>
    public string ComplianceContext { get; init; } = "";
    public string SpanText { get; init; } = "";
    public string FullDraft { get; init; } = "";
    /// <summary>Substring within SpanText the delegated model should prioritize (narrow focus).</summary>
    public string FocusExcerpt { get; init; } = "";
    /// <summary>Extra paragraphs before ParagraphStart included as read-only context in the LLM prompt.</summary>
    public int ContextParagraphsBefore { get; init; }
    /// <summary>Extra paragraphs after ParagraphEnd included as read-only context in the LLM prompt.</summary>
    public int ContextParagraphsAfter { get; init; }
    public string ContextBeforeText { get; init; } = "";
    public string ContextAfterText { get; init; } = "";
}
