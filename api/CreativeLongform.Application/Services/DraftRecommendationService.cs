using System.Linq;
using System.Text;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.DraftRecommendation;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Services;

public sealed class DraftRecommendationService : IDraftRecommendationService
{
    /// <summary>Hard cap so paragraph indices match the analyzed draft and prompts stay bounded.</summary>
    private const int MaxDraftCharsForAnalysis = 100_000;
    private readonly ICreativeLongformDbContext _db;
    private readonly IOllamaClient _ollama;
    private readonly IOllamaModelPreferencesService _modelPrefs;
    private readonly ILogger<DraftRecommendationService> _logger;

    public DraftRecommendationService(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        IOllamaModelPreferencesService modelPrefs,
        ILogger<DraftRecommendationService> logger)
    {
        _db = db;
        _ollama = ollama;
        _modelPrefs = modelPrefs;
        _logger = logger;
    }

    public async Task<DraftRecommendationResultDto> GetRecommendationsAsync(Guid sceneId, string draftText,
        CancellationToken cancellationToken = default)
    {
        var trimmed = draftText.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new ArgumentException("Draft text is required.", nameof(draftText));
        if (trimmed.Length > MaxDraftCharsForAnalysis)
            throw new ArgumentException(
                $"Draft is too long for analysis (max {MaxDraftCharsForAnalysis:N0} characters).");

        var scene = await _db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .ThenInclude(c => c.Book)
            .Include(s => s.SceneWorldElements)
            .ThenInclude(swe => swe.WorldElement)
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            throw new InvalidOperationException("Scene not found.");

        var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
        var worldElementIds = scene.SceneWorldElements.Select(swe => swe.WorldElementId).ToHashSet();
        var scopedLinks = await LoadSceneScopedWorldElementLinksAsync(_db, worldElementIds, cancellationToken);
        var worldBlock = WorldContextBuilder.Build(scene.Chapter.Book, worldElements, scopedLinks);

        var paragraphs = AgenticEditLoop.SplitParagraphs(trimmed);
        if (paragraphs.Count == 0)
            return new DraftRecommendationResultDto();

        var paragraphsForIndex = AgenticEditLoop.SplitParagraphs(trimmed);
        var numberedDraft = BuildNumberedDraft(paragraphsForIndex);

        var model = await _modelPrefs.GetCriticModelAsync(cancellationToken);
        var options = new OllamaChatOptions { NumPredict = 8192 };

        var system = """
            You are an editorial assistant for long-form fiction. You analyze a scene draft and return ONLY valid JSON (no markdown fences).
            You do NOT apply edits. You propose items for a human author to approve.

            Output shape (always include "items" array; may be empty):
            {
              "items": [
                {
                  "kind": "replace",
                  "paragraphStart": 0,
                  "paragraphEnd": 0,
                  "problem": "short description of the issue",
                  "replacementText": "full replacement prose for that inclusive paragraph range; use \\n\\n between paragraphs if multiple"
                },
                {
                  "kind": "rewrite",
                  "paragraphStart": 1,
                  "paragraphEnd": 2,
                  "problem": "what is wrong or weak",
                  "rewriteInstruction": "concrete instruction the author could give to a revision pass (or edit by hand)"
                }
              ]
            }

            Paragraphs are split by blank lines (double newlines), 0-based indices, inclusive range — same as the numbered draft in the user message.

            Rules:
            - Prefer a small number of high-value items (typically 3–10). Empty items if the draft is fine.
            - kind "replace": supply full replacementText only when you can offer complete improved prose for the range.
            - kind "rewrite": use when a targeted revision instruction is better than pasting new prose (voice, pacing, clarity).
            - Check: alignment with scene synopsis/instructions, linked world-building, invention scope (no new named characters/events outside allowed sources), show-vs-tell, dialogue mechanics, pacing, continuity, perspective/tense.
            - Do not propose sanitizing or toning down content for propriety; this is private uncensored fiction — only craft and story-scope issues.
            - replacementText must preserve plot facts and beats from the original span unless fixing a clear contradiction with the brief.
            """;

        var sceneBlock = SceneInstructionsBlock(scene);
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {sceneBlock}

            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

            World context:
            {worldBlock}

            Numbered draft (paragraph indices). Indices 0..{paragraphsForIndex.Count - 1}:
            {numberedDraft}

            Return JSON only.
            """;

        var messages = new List<OllamaChatMessage>
        {
            new("system", system),
            new("user", user)
        };
        var reqJson = JsonSerializer.Serialize(new { model, messages, format = "json", num_predict = options.NumPredict });

        var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options, cancellationToken);

        await _db.LlmCalls.AddAsync(new LlmCall
        {
            Id = Guid.NewGuid(),
            BookId = scene.Chapter.BookId,
            GenerationRunId = null,
            Step = PipelineStep.DraftRecommendationAnalysis,
            Model = model,
            RequestJson = reqJson,
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        DraftRecommendationLlmResult parsed;
        try
        {
            parsed = LlmJson.Deserialize<DraftRecommendationLlmResult>(result.MessageText) ?? new DraftRecommendationLlmResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Draft recommendation JSON parse failed");
            throw new InvalidOperationException("The model did not return valid recommendation JSON.", ex);
        }

        return NormalizeToDto(parsed, paragraphsForIndex.Count);
    }

    private static string SceneInstructionsBlock(Scene scene)
    {
        var syn = scene.Synopsis?.Trim();
        var ins = scene.Instructions?.Trim() ?? "";
        if (string.IsNullOrEmpty(syn))
            return ins;
        if (string.IsNullOrEmpty(ins))
            return syn;
        return $"{syn}\n\nAdditional instructions: {ins}";
    }

    private static string BuildNumberedDraft(IReadOnlyList<string> paragraphs)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            sb.Append('[').Append(i).Append("]\n");
            sb.Append(paragraphs[i]);
            if (i < paragraphs.Count - 1)
                sb.Append("\n\n");
        }

        return sb.ToString();
    }

    private static DraftRecommendationResultDto NormalizeToDto(DraftRecommendationLlmResult parsed, int paragraphCount)
    {
        var maxIdx = Math.Max(0, paragraphCount - 1);
        var list = new List<DraftRecommendationItemDto>();
        foreach (var it in parsed.Items)
        {
            var kind = (it.Kind ?? "").Trim().ToLowerInvariant();
            if (kind != "replace" && kind != "rewrite")
                continue;

            var ps = Math.Clamp(it.ParagraphStart, 0, maxIdx);
            var pe = Math.Clamp(it.ParagraphEnd, 0, maxIdx);
            if (ps > pe)
                (ps, pe) = (pe, ps);

            var problem = (it.Problem ?? "").Trim();
            if (string.IsNullOrEmpty(problem))
                problem = "(no description)";

            if (kind == "replace")
            {
                var rep = (it.ReplacementText ?? "").Trim();
                if (string.IsNullOrEmpty(rep))
                    continue;
                list.Add(new DraftRecommendationItemDto
                {
                    Kind = "replace",
                    ParagraphStart = ps,
                    ParagraphEnd = pe,
                    Problem = problem,
                    ReplacementText = it.ReplacementText?.Trim() ?? ""
                });
            }
            else
            {
                var rw = (it.RewriteInstruction ?? "").Trim();
                if (string.IsNullOrEmpty(rw))
                    continue;
                list.Add(new DraftRecommendationItemDto
                {
                    Kind = "rewrite",
                    ParagraphStart = ps,
                    ParagraphEnd = pe,
                    Problem = problem,
                    RewriteInstruction = rw
                });
            }
        }

        return new DraftRecommendationResultDto { Items = list };
    }

    private static async Task<List<WorldElementLink>> LoadSceneScopedWorldElementLinksAsync(
        ICreativeLongformDbContext db,
        IReadOnlyCollection<Guid> worldElementIds,
        CancellationToken cancellationToken)
    {
        if (worldElementIds.Count == 0)
            return [];
        var ids = worldElementIds as HashSet<Guid> ?? worldElementIds.ToHashSet();
        return await db.WorldElementLinks.AsNoTracking()
            .Where(l => ids.Contains(l.FromWorldElementId) && ids.Contains(l.ToWorldElementId))
            .ToListAsync(cancellationToken);
    }
}
