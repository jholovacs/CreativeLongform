using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

/// <summary>Audit trail when a model assignment changes (UI or API).</summary>
public sealed class OllamaModelChangeLog
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>When the change was recorded.</summary>
    public DateTimeOffset OccurredAt { get; set; }
    public OllamaModelRole Role { get; set; }
    /// <summary>Previous effective model name (after trim), or null if unset.</summary>
    public string? PreviousModel { get; set; }
    /// <summary>New effective model name.</summary>
    public string NewModel { get; set; } = "";
    /// <summary>e.g. ui, api, setup</summary>
    public string Source { get; set; } = "ui";
}
