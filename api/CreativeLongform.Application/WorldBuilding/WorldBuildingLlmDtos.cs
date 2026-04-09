using System.Text.Json.Serialization;

namespace CreativeLongform.Application.WorldBuilding;

public sealed class WorldBuildingBatchResult
{
    [JsonPropertyName("elements")]
    public List<WorldBuildingElementDto> Elements { get; set; } = new();

    [JsonPropertyName("suggestedLinks")]
    public List<WorldBuildingLinkDto> SuggestedLinks { get; set; } = new();
}

public sealed class WorldBuildingElementDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("slug")]
    public string? Slug { get; set; }
}

public sealed class WorldBuildingLinkDto
{
    [JsonPropertyName("fromTitle")]
    public string FromTitle { get; set; } = string.Empty;

    [JsonPropertyName("toTitle")]
    public string ToTitle { get; set; } = string.Empty;

    [JsonPropertyName("relationLabel")]
    public string RelationLabel { get; set; } = string.Empty;
}
