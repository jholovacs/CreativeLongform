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
    WorldBuildingGenerate = 11
}
