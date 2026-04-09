using System.Text.Json.Serialization;

namespace CreativeLongform.Application.WorldBuilding;

/// <summary>LLM JSON for link/timeline canon review.</summary>
public sealed class LinkCanonReviewLlmResult
{
    [JsonPropertyName("proposals")]
    public List<LinkCanonReviewLlmProposal> Proposals { get; set; } = new();
}

public sealed class LinkCanonReviewLlmProposal
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("rationale")]
    public string? Rationale { get; set; }

    [JsonPropertyName("fromTitle")]
    public string? FromTitle { get; set; }

    [JsonPropertyName("toTitle")]
    public string? ToTitle { get; set; }

    [JsonPropertyName("relationLabel")]
    public string? RelationLabel { get; set; }

    [JsonPropertyName("linkId")]
    public Guid? LinkId { get; set; }

    [JsonPropertyName("newRelationLabel")]
    public string? NewRelationLabel { get; set; }

    [JsonPropertyName("timelineEntryId")]
    public Guid? TimelineEntryId { get; set; }

    /// <summary>Exact world element title to attach, or empty string to clear.</summary>
    [JsonPropertyName("worldElementTitle")]
    public string? WorldElementTitle { get; set; }
}
