namespace CreativeLongform.Application.Abstractions;

public interface IWorldBuildingService
{
    Task<WorldBuildingApplyResult> ExtractFromTextAsync(Guid bookId, string text, CancellationToken cancellationToken = default);
    Task<WorldBuildingApplyResult> GenerateFromPromptAsync(Guid bookId, string prompt, CancellationToken cancellationToken = default);
    Task<WorldBuildingApplyResult> BootstrapStoryAsync(Guid bookId, StoryBootstrapRequest request, CancellationToken cancellationToken = default);

    /// <summary>LLM suggests links between one entry and others in the book (not persisted).</summary>
    Task<IReadOnlyList<WorldBuildingSuggestedLink>> SuggestLinksForElementAsync(Guid bookId, Guid elementId,
        CancellationToken cancellationToken = default);

    /// <summary>Persist accepted suggested links (validated for this book).</summary>
    Task<int> ApplySuggestedLinksAsync(Guid bookId, IReadOnlyList<ApplySuggestedLinkItem> links,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads linked elements and related timeline rows, asks the LLM which links/timeline attachments are missing or wrong.
    /// </summary>
    Task<LinkCanonReviewResult> ReviewLinksCanonAsync(Guid bookId, Guid elementId,
        CancellationToken cancellationToken = default);

    /// <summary>Apply user-approved canon review items (links add/remove/relabel, timeline world-element link).</summary>
    Task<LinkCanonApplyResult> ApplyLinkCanonReviewAsync(Guid bookId, IReadOnlyList<ApplyLinkCanonItem> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build markdown glossary for all world elements: A–Z (case-insensitive, leading articles ignored for sort).
    /// Optional LLM pass adds alternate names from titles/summaries; metadata JSON may include <c>alternateNames</c>.
    /// </summary>
    Task<string?> BuildGlossaryMarkdownAsync(Guid bookId, bool useLlmForAlternateNames,
        CancellationToken cancellationToken = default);

    /// <summary>LLM selects world element ids that plausibly appear in the scene synopsis.</summary>
    Task<IReadOnlyList<Guid>> SuggestWorldElementsForSynopsisAsync(Guid bookId, string synopsis,
        CancellationToken cancellationToken = default);
}

public sealed class StoryBootstrapRequest
{
    public string? StoryToneAndStyle { get; set; }
    public string? ContentStyleNotes { get; set; }
    public string? Synopsis { get; set; }
    /// <summary>Optional pasted or imported source text to ground generation.</summary>
    public string? SourceText { get; set; }
}

public sealed class WorldBuildingApplyResult
{
    public IReadOnlyList<Guid> CreatedElementIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Deprecated: links are no longer auto-created; use <see cref="SuggestedLinks"/>.</summary>
    public IReadOnlyList<Guid> CreatedLinkIds { get; init; } = Array.Empty<Guid>();

    /// <summary>Resolved link suggestions for the UI; create with <see cref="IWorldBuildingService.ApplySuggestedLinksAsync"/>.</summary>
    public IReadOnlyList<WorldBuildingSuggestedLink> SuggestedLinks { get; init; } = Array.Empty<WorldBuildingSuggestedLink>();
}

public sealed class WorldBuildingSuggestedLink
{
    public Guid FromWorldElementId { get; set; }
    public Guid ToWorldElementId { get; set; }
    public string FromTitle { get; set; } = string.Empty;
    public string ToTitle { get; set; } = string.Empty;
    public string RelationLabel { get; set; } = string.Empty;
}

public sealed class ApplySuggestedLinkItem
{
    public Guid FromWorldElementId { get; set; }
    public Guid ToWorldElementId { get; set; }
    public string RelationLabel { get; set; } = string.Empty;
}

public sealed class LinkCanonReviewResult
{
    public IReadOnlyList<LinkCanonReviewProposal> Proposals { get; init; } = Array.Empty<LinkCanonReviewProposal>();
}

/// <summary>One actionable proposal for the UI (pre-resolved IDs where applicable).</summary>
public sealed class LinkCanonReviewProposal
{
    public string Id { get; set; } = string.Empty;
    /// <summary>add_link | remove_link | change_relation | set_timeline_link</summary>
    public string Kind { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;

    public string? FromTitle { get; set; }
    public string? ToTitle { get; set; }
    public string? RelationLabel { get; set; }
    public Guid? FromWorldElementId { get; set; }
    public Guid? ToWorldElementId { get; set; }

    public Guid? LinkId { get; set; }
    public string? CurrentRelationLabel { get; set; }
    public string? NewRelationLabel { get; set; }

    public Guid? TimelineEntryId { get; set; }
    public string? TimelineEntryTitle { get; set; }
    public string? CurrentWorldElementTitle { get; set; }
    /// <summary>Resolved target title for display; empty means clear.</summary>
    public string? ProposedWorldElementTitle { get; set; }
    public Guid? ProposedWorldElementId { get; set; }
    public bool ClearWorldElementLink { get; set; }
}

public sealed class ApplyLinkCanonItem
{
    public string Kind { get; set; } = string.Empty;
    public Guid? FromWorldElementId { get; set; }
    public Guid? ToWorldElementId { get; set; }
    public string? RelationLabel { get; set; }
    public Guid? LinkId { get; set; }
    public string? NewRelationLabel { get; set; }
    public Guid? TimelineEntryId { get; set; }
    public Guid? WorldElementId { get; set; }
    public bool ClearWorldElementId { get; set; }
}

public sealed class LinkCanonApplyResult
{
    public int LinksAdded { get; set; }
    public int LinksRemoved { get; set; }
    public int RelationsUpdated { get; set; }
    public int TimelineEntriesUpdated { get; set; }
}
