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
    [JsonPropertyName("pass")]
    public bool Pass { get; set; }

    [JsonPropertyName("issues")]
    public List<string> Issues { get; set; } = new();

    [JsonPropertyName("fixInstructions")]
    public List<string> FixInstructions { get; set; } = new();
}
