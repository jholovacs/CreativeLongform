using CreativeLongform.Application.Abstractions;

namespace CreativeLongform.Infrastructure.Ollama;

public sealed class OllamaBaseUrlProvider : IOllamaBaseUrlProvider
{
    private readonly IOllamaModelPreferencesService _prefs;

    public OllamaBaseUrlProvider(IOllamaModelPreferencesService prefs)
    {
        _prefs = prefs;
    }

    public async Task<string> GetEffectiveBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _prefs.GetConnectionSettingsAsync(cancellationToken);
        return settings.EffectiveBaseUrl;
    }

    public Task<OllamaConnectionSettingsDto> GetConnectionSettingsAsync(CancellationToken cancellationToken = default) =>
        _prefs.GetConnectionSettingsAsync(cancellationToken);
}
