namespace CreativeLongform.Domain.Enums;

/// <summary>Which LLM slot a model name applies to (stored preferences and change log).</summary>
public enum OllamaModelRole
{
    Writer = 0,
    Critic = 1,
    Agent = 2,
    WorldBuilding = 3,
    /// <summary>Beginning narrative state JSON (when not taken from author or prior scene).</summary>
    PreState = 4,
    /// <summary>End-of-scene narrative state JSON derived from prose.</summary>
    PostState = 5,
    /// <summary>Ollama HTTP API base URL.</summary>
    Connection = 6,

    /// <summary>Light prose touch-ups: tense/perspective shifts, formatting, user-specified decoration.</summary>
    Editor = 7,

    /// <summary>Prose craft quality scoring (separate from compliance critic when configured).</summary>
    QualityCritic = 8
}
