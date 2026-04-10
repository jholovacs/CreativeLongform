namespace CreativeLongform.Domain.Entities;

/// <summary>
/// Smallest narrative unit for generation: synopsis, instructions, voice, state JSON, draft and manuscript text, and links to
/// world elements. Drives the pipeline (<see cref="GenerationRun"/>) and timeline entries for story order.
/// </summary>
public class Scene
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>FK to parent chapter.</summary>
    public Guid ChapterId { get; set; }
    /// <summary>Navigation to parent chapter.</summary>
    public Chapter Chapter { get; set; } = null!;
    /// <summary>Order within the chapter (unique per chapter).</summary>
    public int Order { get; set; }
    /// <summary>Short label for UI, timeline, and assembled manuscript scene headings.</summary>
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
    /// <summary>Accepted prose after finalize (not overwritten by a new generation run).</summary>
    public string? ManuscriptText { get; set; }
    /// <summary>Generation attempts for this scene (draft, repair, finalize).</summary>
    public ICollection<GenerationRun> GenerationRuns { get; set; } = new List<GenerationRun>();
    /// <summary>World-building entries linked for prompts (scene-scoped canon).</summary>
    public ICollection<SceneWorldElement> SceneWorldElements { get; set; } = new List<SceneWorldElement>();
    /// <summary>Optional 1:1 timeline row for this scene&apos;s story-time position.</summary>
    public TimelineEntry? TimelineEntry { get; set; }
}
