using System.Text.Json.Serialization;

namespace CreativeLongform.Application.DraftRecommendation;

public sealed class DraftRecommendationsRequest
{
    /// <summary>Scene draft to analyze (typically the textarea in review).</summary>
    public string DraftText { get; set; } = "";
}

public sealed class DraftRecommendationResultDto
{
    public List<DraftRecommendationItemDto> Items { get; set; } = new();
}

public sealed class DraftRecommendationItemDto
{
    /// <summary>replace = full replacement prose for the range; rewrite = instruction-only for the author to run Correct or edit manually.</summary>
    public string Kind { get; set; } = "";

    public int ParagraphStart { get; set; }
    public int ParagraphEnd { get; set; }

    /// <summary>What is wrong or could be better (for the author).</summary>
    public string Problem { get; set; } = "";

    /// <summary>When kind is replace: full replacement for paragraphs ParagraphStart..ParagraphEnd (may use \n\n for multiple paragraphs).</summary>
    public string? ReplacementText { get; set; }

    /// <summary>When kind is rewrite: concrete instruction for a corrective pass (e.g. Correct With LLM).</summary>
    public string? RewriteInstruction { get; set; }
}

/// <summary>LLM JSON shape (deserialization).</summary>
internal sealed class DraftRecommendationLlmResult
{
    [JsonPropertyName("items")]
    public List<DraftRecommendationLlmItem> Items { get; set; } = new();
}

internal sealed class DraftRecommendationLlmItem
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("paragraphStart")]
    public int ParagraphStart { get; set; }

    [JsonPropertyName("paragraphEnd")]
    public int ParagraphEnd { get; set; }

    [JsonPropertyName("problem")]
    public string? Problem { get; set; }

    [JsonPropertyName("replacementText")]
    public string? ReplacementText { get; set; }

    [JsonPropertyName("rewriteInstruction")]
    public string? RewriteInstruction { get; set; }
}
