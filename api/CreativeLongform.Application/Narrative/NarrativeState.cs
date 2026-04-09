using System.Text.Json.Serialization;

namespace CreativeLongform.Application.Narrative;

/// <summary>Structured snapshot for continuity (serialized to StateJson).</summary>
public sealed class NarrativeState
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("transitionSummary")]
    public string? TransitionSummary { get; set; }

    [JsonPropertyName("characters")]
    public List<CharacterState> Characters { get; set; } = new();

    [JsonPropertyName("spatial")]
    public SpatialState? Spatial { get; set; }

    [JsonPropertyName("dialogue")]
    public DialogueThreadState? Dialogue { get; set; }

    [JsonPropertyName("knowledge")]
    public KnowledgeState? Knowledge { get; set; }

    [JsonPropertyName("environment")]
    public EnvironmentState? Environment { get; set; }

    [JsonPropertyName("plotDevices")]
    public List<string> PlotDevices { get; set; } = new();
}

public sealed class CharacterState
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("pose")]
    public string? Pose { get; set; }

    [JsonPropertyName("clothing")]
    public string? Clothing { get; set; }

    [JsonPropertyName("emotionalState")]
    public string? EmotionalState { get; set; }

    [JsonPropertyName("traitsShownNotTold")]
    public List<string> TraitsShownNotTold { get; set; } = new();
}

public sealed class SpatialState
{
    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("proximity")]
    public string? Proximity { get; set; }
}

public sealed class DialogueThreadState
{
    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("unresolved")]
    public List<string> Unresolved { get; set; } = new();
}

public sealed class KnowledgeState
{
    [JsonPropertyName("povBeliefs")]
    public List<string> PovBeliefs { get; set; } = new();

    [JsonPropertyName("omniscientFacts")]
    public List<string> OmniscientFacts { get; set; } = new();
}

public sealed class EnvironmentState
{
    [JsonPropertyName("timeOfDay")]
    public string? TimeOfDay { get; set; }

    [JsonPropertyName("weather")]
    public string? Weather { get; set; }

    [JsonPropertyName("sensory")]
    public List<string> Sensory { get; set; } = new();
}
