namespace CreativeLongform.Application.Agent;

/// <summary>
/// Model roles in the agent architecture. The orchestrator agent delegates; critics verify.
/// See <c>AGENTS.md</c> at the repository root for the full specification.
/// </summary>
public static class AgentRoles
{
    /// <summary>JSON tool loop — plans, chooses tools, verifies, and finishes.</summary>
    public const string Orchestrator = "Agent";

    /// <summary>Creative prose rewrite for a paragraph span (<c>invoke_writer</c>).</summary>
    public const string Writer = "Writer";

    /// <summary>Light touch-ups: tense, POV, formatting (<c>invoke_editor</c>).</summary>
    public const string Editor = "Editor";

    /// <summary>Grammar, spelling, punctuation (<c>invoke_corrector</c>).</summary>
    public const string Corrector = "Corrector";

    /// <summary>Instruction compliance critic (<c>run_compliance_check</c>).</summary>
    public const string ComplianceCritic = "ComplianceCritic";

    /// <summary>Prose quality critic (<c>run_quality_check</c>).</summary>
    public const string QualityCritic = "QualityCritic";
}
