using System.Text.Json.Serialization;

namespace CreativeLongform.Application.Generation;

public sealed class TransitionVerdict
{
    [JsonPropertyName("pass")]
    public bool Pass { get; set; }

    [JsonPropertyName("gaps")]
    public List<string> Gaps { get; set; } = new();
}

public sealed class ComplianceVerdict
{
    [JsonPropertyName("pass")]
    public bool Pass { get; set; }

    [JsonPropertyName("violations")]
    public List<string> Violations { get; set; } = new();

    [JsonPropertyName("fixInstructions")]
    public List<string> FixInstructions { get; set; } = new();
}

public sealed class QualityVerdict
{
    /// <summary>0–100 (higher is better). Primary gate signal.</summary>
    [JsonPropertyName("score")]
    public double? Score { get; set; }

    /// <summary>Legacy; ignored when <see cref="Score"/> is set.</summary>
    [JsonPropertyName("pass")]
    public bool? Pass { get; set; }

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();

    [JsonPropertyName("fixInstructions")]
    public List<string> FixInstructions { get; set; } = new();
}

/// <summary>LLM output when replacing only a selected range of the draft (selectionEnd exclusive, same as HTML textarea).</summary>
public sealed class DraftReplacementJson
{
    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = "";
}
