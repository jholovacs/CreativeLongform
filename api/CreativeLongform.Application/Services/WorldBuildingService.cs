using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Options;
using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Application.Services;

public sealed class WorldBuildingService : IWorldBuildingService
{
    private readonly ICreativeLongformDbContext _db;
    private readonly IOllamaClient _ollama;
    private readonly IOptions<OllamaOptions> _ollamaOptions;
    private readonly ILogger<WorldBuildingService> _logger;

    public WorldBuildingService(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        IOptions<OllamaOptions> ollamaOptions,
        ILogger<WorldBuildingService> logger)
    {
        _db = db;
        _ollama = ollama;
        _ollamaOptions = ollamaOptions;
        _logger = logger;
    }

    public Task<WorldBuildingApplyResult> ExtractFromTextAsync(Guid bookId, string text, CancellationToken cancellationToken = default) =>
        RunAsync(bookId, text, isExtract: true, cancellationToken);

    public Task<WorldBuildingApplyResult> GenerateFromPromptAsync(Guid bookId, string prompt, CancellationToken cancellationToken = default) =>
        RunAsync(bookId, prompt, isExtract: false, cancellationToken);

    private async Task<WorldBuildingApplyResult> RunAsync(Guid bookId, string input, bool isExtract, CancellationToken cancellationToken)
    {
        await _db.Books.AsNoTracking().FirstAsync(b => b.Id == bookId, cancellationToken);

        var model = _ollamaOptions.Value.WriterModel;
        var step = isExtract ? PipelineStep.WorldBuildingExtract : PipelineStep.WorldBuildingGenerate;

        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: { "elements": [ { "kind": string, "title": string, "summary": string, "detail": string|null, "slug": string|null } ], "suggestedLinks": [ { "fromTitle": string, "toTitle": string, "relationLabel": string } ] }.
            kind must be one of: Geography, Culture, Lore, Law, SignificantEvent, SocialSystem, NovelSystem, Other.
            """;
        var user = isExtract
            ? $"""
            Extract distinct world-building facts from the text below. Merge duplicates; keep summaries concise.
            Text:
            {input}
            """
            : $"""
            The author requests new world-building content. Generate coherent entries that fit together.
            Author prompt:
            {input}
            """;

        var messages = new List<OllamaChatMessage>
        {
            new("system", system),
            new("user", user)
        };
        var req = JsonSerializer.Serialize(new { model, messages, format = "json" });
        var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options: null, cancellationToken);
        await _db.LlmCalls.AddAsync(new LlmCall
        {
            Id = Guid.NewGuid(),
            GenerationRunId = null,
            BookId = bookId,
            Step = step,
            Model = model,
            RequestJson = req,
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var batch = LlmJson.Deserialize<WorldBuildingBatchResult>(result.MessageText)
                    ?? new WorldBuildingBatchResult();

        var createdElements = new List<Guid>();
        var titleToId = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        foreach (var dto in batch.Elements)
        {
            if (string.IsNullOrWhiteSpace(dto.Title))
                continue;
            var kind = ParseKind(dto.Kind);
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            _db.WorldElements.Add(new WorldElement
            {
                Id = id,
                BookId = bookId,
                Kind = kind,
                Title = dto.Title.Trim(),
                Slug = string.IsNullOrWhiteSpace(dto.Slug) ? null : dto.Slug.Trim(),
                Summary = dto.Summary.Trim(),
                Detail = string.IsNullOrWhiteSpace(dto.Detail) ? string.Empty : dto.Detail.Trim(),
                Status = WorldElementStatus.Draft,
                Provenance = isExtract ? WorldElementProvenance.LlmExtracted : WorldElementProvenance.LlmGenerated,
                MetadataJson = null,
                CreatedAt = now,
                UpdatedAt = now
            });
            createdElements.Add(id);
            titleToId[dto.Title.Trim()] = id;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var createdLinks = new List<Guid>();
        foreach (var link in batch.SuggestedLinks)
        {
            if (string.IsNullOrWhiteSpace(link.FromTitle) || string.IsNullOrWhiteSpace(link.ToTitle))
                continue;
            if (!titleToId.TryGetValue(link.FromTitle.Trim(), out var fromId))
                continue;
            if (!titleToId.TryGetValue(link.ToTitle.Trim(), out var toId))
                continue;
            if (fromId == toId)
                continue;

            var linkId = Guid.NewGuid();
            _db.WorldElementLinks.Add(new WorldElementLink
            {
                Id = linkId,
                FromWorldElementId = fromId,
                ToWorldElementId = toId,
                RelationLabel = link.RelationLabel.Trim()
            });
            createdLinks.Add(linkId);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var parseNote = batch.Elements.Count == 0;
        if (parseNote)
            _logger.LogWarning("World-building batch parsed zero elements for book {BookId}", bookId);

        return new WorldBuildingApplyResult
        {
            CreatedElementIds = createdElements,
            CreatedLinkIds = createdLinks
        };
    }

    private static WorldElementKind ParseKind(string raw)
    {
        if (Enum.TryParse<WorldElementKind>(raw, ignoreCase: true, out var k))
            return k;
        return WorldElementKind.Other;
    }
}
