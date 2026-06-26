using CreativeLongform.Application.Ollama;

namespace CreativeLongform.Application.Abstractions;

public interface IOllamaClient
{
    Task<OllamaChatResult> ChatAsync(
        string model,
        IReadOnlyList<OllamaChatMessage> messages,
        bool jsonFormat,
        OllamaChatOptions? options = null,
        Func<OllamaStreamUpdate, CancellationToken, Task>? onStreamUpdate = null,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed record OllamaChatMessage(string Role, string Content);

/// <summary>Optional Ollama generation parameters (e.g. num_predict for long prose).</summary>
public sealed record OllamaChatOptions
{
    /// <summary>Max tokens to generate (Ollama num_predict).</summary>
    public int? NumPredict { get; init; }

    /// <summary>Ollama repeat_penalty (typical 1.0–1.2; higher discourages token loops).</summary>
    public float? RepeatPenalty { get; init; }

    /// <summary>Ollama repeat_last_n — window of recent tokens penalized for repetition.</summary>
    public int? RepeatLastN { get; init; }

    /// <summary>Ollama temperature (lower values for structured JSON).</summary>
    public float? Temperature { get; init; }
}

public sealed record OllamaChatResult(string Model, string MessageText);
