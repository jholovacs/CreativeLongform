using System.Text.Json.Serialization;

namespace CreativeLongform.Application.WorldBuilding;

/// <summary>LLM JSON for glossary alternate names.</summary>
public sealed class GlossaryAlternateLlmResult
{
    [JsonPropertyName("entries")]
    public List<GlossaryAlternateLlmEntry> Entries { get; set; } = new();
}

public sealed class GlossaryAlternateLlmEntry
{
    [JsonPropertyName("elementId")]
    public Guid ElementId { get; set; }

    [JsonPropertyName("alternateNames")]
    public List<string> AlternateNames { get; set; } = new();
}

/// <summary>Optional <see cref="Domain.Entities.WorldElement.MetadataJson"/> shape for hand-authored alternates.</summary>
public sealed class WorldElementMetadataGlossary
{
    [JsonPropertyName("alternateNames")]
    public List<string>? AlternateNames { get; set; }
}
