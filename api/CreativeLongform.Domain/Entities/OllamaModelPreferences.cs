namespace CreativeLongform.Domain.Entities;

/// <summary>Singleton row (Id = 1): DB overrides for Ollama model names. Null column = use appsettings default.</summary>
public sealed class OllamaModelPreferences
{
    public const int SingletonId = 1;

    public int Id { get; set; }

    /// <summary>Creative prose: draft, expansion passes.</summary>
    public string? WriterModel { get; set; }

    /// <summary>Compliance, quality, transition checks, repair passes.</summary>
    public string? CriticModel { get; set; }

    /// <summary>Agentic edit loop (JSON tools). Null = use writer effective model.</summary>
    public string? AgentModel { get; set; }

    /// <summary>Book/world LLM features. Null = use writer effective model.</summary>
    public string? WorldBuildingModel { get; set; }

    /// <summary>Pre-scene narrative state JSON (when not from author or prior scene). Null = use writer effective model.</summary>
    public string? PreStateModel { get; set; }

    /// <summary>Post-scene narrative state JSON from prose. Null = use writer effective model.</summary>
    public string? PostStateModel { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
