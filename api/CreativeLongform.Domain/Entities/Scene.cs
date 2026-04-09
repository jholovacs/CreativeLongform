namespace CreativeLongform.Domain.Entities;

public class Scene
{
    public Guid Id { get; set; }
    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>Author instructions for the scene (brief).</summary>
    public string Instructions { get; set; } = string.Empty;
    /// <summary>Optional notes on how the scene should end (state / beats).</summary>
    public string? ExpectedEndStateNotes { get; set; }
    /// <summary>Last successful or in-progress draft text.</summary>
    public string? LatestDraftText { get; set; }
    public ICollection<GenerationRun> GenerationRuns { get; set; } = new List<GenerationRun>();
    public ICollection<SceneWorldElement> SceneWorldElements { get; set; } = new List<SceneWorldElement>();
}
