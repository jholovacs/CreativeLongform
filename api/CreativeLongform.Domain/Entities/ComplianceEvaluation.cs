namespace CreativeLongform.Domain.Entities;

public class ComplianceEvaluation
{
    public Guid Id { get; set; }
    public Guid GenerationRunId { get; set; }
    public GenerationRun GenerationRun { get; set; } = null!;
    public bool Passed { get; set; }
    /// <summary>Compliance, Quality, or Transition.</summary>
    public string Kind { get; set; } = string.Empty;
    public int AttemptNumber { get; set; }
    public string VerdictJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
