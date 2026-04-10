using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Application.Abstractions;

public interface IOllamaModelPreferencesService
{
    Task<OllamaModelAssignmentsDto> GetAssignmentsAsync(CancellationToken cancellationToken = default);

    Task<OllamaModelAssignmentsDto> UpdateAssignmentsAsync(
        OllamaModelAssignmentsPatch patch,
        string source,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OllamaModelChangeLogDto>> GetChangeLogAsync(int take, CancellationToken cancellationToken = default);

    Task<string> GetWriterModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetCriticModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetAgentModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetWorldBuildingModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetPreStateModelAsync(CancellationToken cancellationToken = default);
    Task<string> GetPostStateModelAsync(CancellationToken cancellationToken = default);
}

public sealed class OllamaModelAssignmentsDto
{
    public string WriterModel { get; init; } = "";
    public string CriticModel { get; init; } = "";
    public string AgentModel { get; init; } = "";
    public string WorldBuildingModel { get; init; } = "";
    public string PreStateModel { get; init; } = "";
    public string PostStateModel { get; init; } = "";
    /// <summary>Per-role: value comes from DB override vs configuration default.</summary>
    public OllamaModelRole[] DbOverriddenRoles { get; init; } = [];
}

public sealed class OllamaModelAssignmentsPatch
{
    public string? WriterModel { get; set; }
    public string? CriticModel { get; set; }
    public string? AgentModel { get; set; }
    public string? WorldBuildingModel { get; set; }
    public string? PreStateModel { get; set; }
    public string? PostStateModel { get; set; }
    /// <summary>When true, clear DB override for that slot (fall back to appsettings).</summary>
    public bool? ClearWriter { get; set; }
    public bool? ClearCritic { get; set; }
    public bool? ClearAgent { get; set; }
    public bool? ClearWorldBuilding { get; set; }
    public bool? ClearPreState { get; set; }
    public bool? ClearPostState { get; set; }
}

public sealed class OllamaModelChangeLogDto
{
    public Guid Id { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public OllamaModelRole Role { get; init; }
    public string? PreviousModel { get; init; }
    public string NewModel { get; init; } = "";
    public string Source { get; init; } = "";
}
