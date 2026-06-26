namespace CreativeLongform.Application.Agent;

/// <summary>Why the agent loop is running. Drives mission text, verification policy, and progress labels.</summary>
public enum AgentSessionKind
{
    /// <summary>Post-draft refinement in the generation pipeline (after initial writer output).</summary>
    PipelinePostDraft,

    /// <summary>Author-driven correction during draft review (Correct With LLM).</summary>
    AuthorCorrection
}
