namespace CreativeLongform.Domain.Enums;

public enum PipelineStep
{
    PreState = 0,
    Draft = 1,
    PostState = 2,
    TransitionCheck = 3,
    Compliance = 4,
    Quality = 5,
    Repair = 6,
    /// <summary>Iterative JSON tool loop (read_section, propose_patch, finish).</summary>
    AgentEdit = 7,
    WorldBuildingExtract = 10,
    WorldBuildingGenerate = 11,
    /// <summary>LLM suggests relations for a single world element vs. others in the book.</summary>
    WorldBuildingLinkSuggest = 12,

    /// <summary>LLM reviews links and timeline attachments for one element vs. neighbors.</summary>
    WorldBuildingLinkCanonReview = 13,

    /// <summary>LLM proposes alternate names for glossary export.</summary>
    WorldBuildingGlossary = 14,

    /// <summary>LLM picks world elements relevant to a scene synopsis.</summary>
    SceneSynopsisWorldElements = 15
}
