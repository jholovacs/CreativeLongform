namespace CreativeLongform.Domain.Entities;

/// <summary>
/// One compliance or quality verdict attempt against a <see cref="GenerationRun"/> (structured JSON for repair logic).
/// </summary>
public class ComplianceEvaluation
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to generation run.</summary>
    public Guid GenerationRunId { get; set; }
    /// <summary>Navigation to run.</summary>
    public GenerationRun GenerationRun { get; set; } = null!;
    /// <summary>Whether this check passed.</summary>
    public bool Passed { get; set; }
    /// <summary>Compliance, Quality, or Transition.</summary>
    public string Kind { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string VerdictJson { get; set; } = "{}";
    /// <summary>When evaluated.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
