namespace CreativeLongform.Domain.Entities;

public class Scene
{
    public Guid Id { get; set; }
    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    /// <summary>What happens in this scene (plot beat); primary brief for generation.</summary>
    public string Synopsis { get; set; } = string.Empty;
    /// <summary>Author instructions for the scene (brief).</summary>
    public string Instructions { get; set; } = string.Empty;
    /// <summary>e.g. third limited, third omniscient, first person.</summary>
    public string? NarrativePerspective { get; set; }
    /// <summary>e.g. past, present.</summary>
    public string? NarrativeTense { get; set; }
    /// <summary>Optional JSON narrative state before this scene (overrides chain from previous scene).</summary>
    public string? BeginningStateJson { get; set; }
    /// <summary>Last approved end-state JSON for this scene (after user accepts generation).</summary>
    public string? ApprovedStateTableJson { get; set; }
    /// <summary>Latest LLM-derived post-scene state from draft review (not yet finalized); cleared when the user finalizes or starts a new run.</summary>
    public string? PendingPostStateJson { get; set; }
    /// <summary>Optional notes on how the scene should end (state / beats).</summary>
    public string? ExpectedEndStateNotes { get; set; }
    /// <summary>Last successful or in-progress draft text.</summary>
    public string? LatestDraftText { get; set; }
    public ICollection<GenerationRun> GenerationRuns { get; set; } = new List<GenerationRun>();
    public ICollection<SceneWorldElement> SceneWorldElements { get; set; } = new List<SceneWorldElement>();
    public TimelineEntry? TimelineEntry { get; set; }
}
