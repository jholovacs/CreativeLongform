namespace CreativeLongform.Application.Abstractions;

public interface IOllamaClient
{
    Task<OllamaChatResult> ChatAsync(
        string model,
        IReadOnlyList<OllamaChatMessage> messages,
        bool jsonFormat,
        OllamaChatOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed record OllamaChatMessage(string Role, string Content);

/// <summary>Optional Ollama generation parameters (e.g. num_predict for long prose).</summary>
public sealed record OllamaChatOptions
{
    /// <summary>Max tokens to generate (Ollama num_predict).</summary>
    public int? NumPredict { get; init; }
}

public sealed record OllamaChatResult(string Model, string MessageText);
