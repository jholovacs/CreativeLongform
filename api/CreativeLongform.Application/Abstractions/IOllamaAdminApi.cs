namespace CreativeLongform.Application.Abstractions;

/// <summary>Low-level Ollama HTTP API (tags, pull, create) for admin UI.</summary>
public interface IOllamaAdminApi
{
    /// <summary>Installed models with sizes from <c>GET /api/tags</c> and optional VRAM from <c>GET /api/ps</c>.</summary>
    Task<IReadOnlyList<OllamaLocalModelInfo>> ListLocalModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListModelNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Pull a model from the Ollama library (e.g. llama3.2).</summary>
    Task PullAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a library pull with <c>stream: true</c> (NDJSON lines) into <paramref name="output"/>.
    /// Caller sets HTTP response headers and disables buffering.
    /// </summary>
    Task StreamPullAsync(string modelName, Stream output, CancellationToken cancellationToken = default);

    /// <summary>Delete a model from local disk (Ollama <c>DELETE /api/delete</c>).</summary>
    Task DeleteModelAsync(string modelName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a GGUF already visible to the Ollama process at <paramref name="ggufAbsolutePath"/> as <paramref name="modelName"/>.
    /// </summary>
    Task CreateFromGgufFileAsync(string modelName, string ggufAbsolutePath, CancellationToken cancellationToken = default);
}
