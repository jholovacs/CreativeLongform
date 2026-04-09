using System.Text.Json.Serialization;

namespace CreativeLongform.Application.WorldBuilding;

public sealed class SceneSynopsisWorldElementsLlmResult
{
    [JsonPropertyName("elementIds")]
    public List<string> ElementIds { get; set; } = new();
}
