namespace CreativeLongform.Application.Abstractions;

/// <summary>One row from Ollama <c>GET /api/tags</c>, optionally merged with <c>GET /api/ps</c> for VRAM.</summary>
public sealed class OllamaLocalModelInfo
{
    public string Name { get; init; } = "";

    /// <summary>Total on-disk size of model blobs (bytes).</summary>
    public long SizeBytes { get; init; }

    /// <summary>e.g. <c>7.6B</c> from Ollama <c>details.parameter_size</c>.</summary>
    public string? ParameterSize { get; init; }

    /// <summary>e.g. <c>Q4_K_M</c> from Ollama <c>details.quantization_level</c>.</summary>
    public string? QuantizationLevel { get; init; }

    /// <summary>VRAM usage when the model is loaded (<c>GET /api/ps</c> <c>size_vram</c>); null if not in memory.</summary>
    public long? VramBytes { get; init; }
}
