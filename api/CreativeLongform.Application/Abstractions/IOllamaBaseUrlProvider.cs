namespace CreativeLongform.Application.Abstractions;

public interface IOllamaBaseUrlProvider
{
    Task<string> GetEffectiveBaseUrlAsync(CancellationToken cancellationToken = default);

    Task<OllamaConnectionSettingsDto> GetConnectionSettingsAsync(CancellationToken cancellationToken = default);
}

public sealed class OllamaConnectionSettingsDto
{
    /// <summary>URL the API uses for Ollama requests (DB override or configuration default).</summary>
    public string EffectiveBaseUrl { get; init; } = "";

    /// <summary>appsettings / environment default before DB override.</summary>
    public string ConfigurationDefaultBaseUrl { get; init; } = "";

    /// <summary>Stored DB override when set.</summary>
    public string? DbOverrideBaseUrl { get; init; }

    public bool IsDbOverridden { get; init; }
}
