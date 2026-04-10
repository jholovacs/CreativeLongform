using CreativeLongform.Application.DraftRecommendation;

namespace CreativeLongform.Application.Abstractions;

public interface IDraftRecommendationService
{
    /// <summary>
    /// Analyzes draft prose in the context of the scene and world; returns proposed edits for author approval only (not applied server-side).
    /// </summary>
    Task<DraftRecommendationResultDto> GetRecommendationsAsync(Guid sceneId, string draftText,
        CancellationToken cancellationToken = default);
}
