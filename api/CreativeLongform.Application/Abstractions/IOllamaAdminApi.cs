namespace CreativeLongform.Application.Abstractions;

/// <summary>Low-level Ollama HTTP API (tags, pull, create) for admin UI.</summary>
public interface IOllamaAdminApi
{
    Task<IReadOnlyList<string>> ListModelNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Pull a model from the Ollama library (e.g. llama3.2).</summary>
    Task PullAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a GGUF already visible to the Ollama process at <paramref name="ggufAbsolutePath"/> as <paramref name="modelName"/>.
    /// </summary>
    Task CreateFromGgufFileAsync(string modelName, string ggufAbsolutePath, CancellationToken cancellationToken = default);
}
