using CreativeLongform.Domain.Enums;

namespace CreativeLongform.Domain.Entities;

public class LlmCall
{
    public Guid Id { get; set; }
    public Guid? GenerationRunId { get; set; }
    public GenerationRun? GenerationRun { get; set; }
    /// <summary>When set, audit for world-building LLM calls not tied to a generation run.</summary>
    public Guid? BookId { get; set; }
    public Book? Book { get; set; }
    public PipelineStep Step { get; set; }
    public string Model { get; set; } = string.Empty;
    public string RequestJson { get; set; } = "{}";
    public string ResponseText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
