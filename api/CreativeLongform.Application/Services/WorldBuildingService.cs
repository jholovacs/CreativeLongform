using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public async Task<WorldBuildingApplyResult> BootstrapStoryAsync(Guid bookId, StoryBootstrapRequest request,
        CancellationToken cancellationToken = default)
    {
        var book = await _db.Books.FirstAsync(b => b.Id == bookId, cancellationToken);
        if (request.StoryToneAndStyle is not null)
            book.StoryToneAndStyle = request.StoryToneAndStyle;
        if (request.ContentStyleNotes is not null)
            book.ContentStyleNotes = request.ContentStyleNotes;
        if (request.Synopsis is not null)
            book.Synopsis = request.Synopsis;
        await _db.SaveChangesAsync(cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Story title: {book.Title}");
        if (!string.IsNullOrWhiteSpace(book.StoryToneAndStyle))
            sb.AppendLine($"Tone and style: {book.StoryToneAndStyle}");
        if (!string.IsNullOrWhiteSpace(book.ContentStyleNotes))
            sb.AppendLine($"Content / style notes: {book.ContentStyleNotes}");
        if (!string.IsNullOrWhiteSpace(book.Synopsis))
            sb.AppendLine($"Synopsis: {book.Synopsis}");
        if (!string.IsNullOrWhiteSpace(request.SourceText))
        {
            sb.AppendLine();
            sb.AppendLine("Additional source text from the author:");
            sb.AppendLine(request.SourceText.Trim());
        }

        sb.AppendLine();
        sb.AppendLine(
            "Generate a coherent set of world-building entries and key characters that fit this story. " +
            "Include locations, cultures, institutions, and major characters as appropriate. " +
            "Use kind \"Character\" for people (named roles, protagonists, antagonists). " +
            "Suggest links between related entries.");

        return await RunAsync(bookId, sb.ToString(), isExtract: false, cancellationToken);
    }

    private async Task<WorldBuildingApplyResult> RunAsync(Guid bookId, string input, bool isExtract, CancellationToken cancellationToken)
    {
        await _db.Books.AsNoTracking().FirstAsync(b => b.Id == bookId, cancellationToken);

        var model = _ollamaOptions.Value.WriterModel;
        var step = isExtract ? PipelineStep.WorldBuildingExtract : PipelineStep.WorldBuildingGenerate;

        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: { "elements": [ { "kind": string, "title": string, "summary": string, "detail": string|null, "slug": string|null } ], "suggestedLinks": [ { "fromTitle": string, "toTitle": string, "relationLabel": string } ] }.
            kind must be one of: Geography, Culture, Lore, Law, SignificantEvent, SocialSystem, NovelSystem, Character, Other.
            Use Character for people and named figures; use other kinds for places, institutions, events, and systems.
            For suggestedLinks, relationLabel must be short plain English for readers (e.g. "Located in", "Knows", "Rules over"). Never use snake_case, underscores, or machine-style identifiers.
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
        }

        await _db.SaveChangesAsync(cancellationToken);

        var allInBook = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId)
            .ToListAsync(cancellationToken);
        var titleMap = BuildTitleToIdMap(allInBook);
        var suggestedResolved = ResolveSuggestedLinks(batch.SuggestedLinks, titleMap);

        var parseNote = batch.Elements.Count == 0;
        if (parseNote)
            _logger.LogWarning("World-building batch parsed zero elements for book {BookId}", bookId);

        return new WorldBuildingApplyResult
        {
            CreatedElementIds = createdElements,
            CreatedLinkIds = Array.Empty<Guid>(),
            SuggestedLinks = suggestedResolved
        };
    }

    public async Task<IReadOnlyList<WorldBuildingSuggestedLink>> SuggestLinksForElementAsync(Guid bookId, Guid elementId,
        CancellationToken cancellationToken = default)
    {
        var focus = await _db.WorldElements.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == elementId && w.BookId == bookId, cancellationToken);
        if (focus is null)
            return Array.Empty<WorldBuildingSuggestedLink>();

        var others = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId && w.Id != elementId)
            .OrderBy(w => w.Title)
            .ToListAsync(cancellationToken);

        if (others.Count == 0)
            return Array.Empty<WorldBuildingSuggestedLink>();

        var model = _ollamaOptions.Value.WriterModel;
        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: { "suggestedLinks": [ { "fromTitle": string, "toTitle": string, "relationLabel": string } ] }.
            Suggest 0–12 meaningful directed relations. Each link must involve the FOCUSED entry (one end is the focused title).
            relationLabel must be short plain English for human readers (e.g. "Located in", "Married to", "Owes allegiance to"). Never use snake_case or underscores.
            Use EXACT titles from the lists below for fromTitle and toTitle.
            """;

        var sb = new StringBuilder();
        sb.AppendLine("FOCUSED entry (must appear as fromTitle or toTitle in every suggested link):");
        sb.AppendLine($"Title: {focus.Title}");
        sb.AppendLine($"Kind: {focus.Kind}");
        sb.AppendLine($"Summary: {Truncate(focus.Summary, 600)}");
        sb.AppendLine();
        sb.AppendLine("Other entries in this book:");
        foreach (var o in others)
        {
            sb.AppendLine($"- Title: {o.Title} | Kind: {o.Kind} | Summary: {Truncate(o.Summary, 400)}");
        }

        var messages = new List<OllamaChatMessage>
        {
            new("system", system),
            new("user", sb.ToString())
        };
        var req = JsonSerializer.Serialize(new { model, messages, format = "json" });
        var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options: null, cancellationToken);
        await _db.LlmCalls.AddAsync(new LlmCall
        {
            Id = Guid.NewGuid(),
            GenerationRunId = null,
            BookId = bookId,
            Step = PipelineStep.WorldBuildingLinkSuggest,
            Model = model,
            RequestJson = req,
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var parsed = LlmJson.Deserialize<WorldBuildingLinkSuggestResult>(result.MessageText)
                     ?? new WorldBuildingLinkSuggestResult();
        var allElements = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId)
            .ToListAsync(cancellationToken);
        var map = BuildTitleToIdMap(allElements);
        return ResolveSuggestedLinks(parsed.SuggestedLinks, map);
    }

    public async Task<int> ApplySuggestedLinksAsync(Guid bookId, IReadOnlyList<ApplySuggestedLinkItem> links,
        CancellationToken cancellationToken = default)
    {
        if (links.Count == 0)
            return 0;

        var created = 0;
        foreach (var item in links)
        {
            if (item.FromWorldElementId == Guid.Empty || item.ToWorldElementId == Guid.Empty ||
                item.FromWorldElementId == item.ToWorldElementId)
                continue;
            var label = HumanizeRelationLabel((item.RelationLabel ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(label))
                continue;

            var fromOk = await _db.WorldElements.AsNoTracking()
                .AnyAsync(w => w.Id == item.FromWorldElementId && w.BookId == bookId, cancellationToken);
            var toOk = await _db.WorldElements.AsNoTracking()
                .AnyAsync(w => w.Id == item.ToWorldElementId && w.BookId == bookId, cancellationToken);
            if (!fromOk || !toOk)
                continue;

            _db.WorldElementLinks.Add(new WorldElementLink
            {
                Id = Guid.NewGuid(),
                FromWorldElementId = item.FromWorldElementId,
                ToWorldElementId = item.ToWorldElementId,
                RelationLabel = label
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                created++;
            }
            catch (DbUpdateException)
            {
                // duplicate (from, to, label) — skip
            }
        }

        return created;
    }

    /// <summary>Turns legacy snake_case labels into readable phrases for display and storage.</summary>
    private static string HumanizeRelationLabel(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        raw = raw.Trim();
        if (!raw.Contains('_', StringComparison.Ordinal))
            return raw;
        var parts = raw.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return raw;
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length == 0)
                continue;
            parts[i] = char.ToUpperInvariant(p[0]) + (p.Length > 1 ? p[1..].ToLowerInvariant() : string.Empty);
        }

        return string.Join(' ', parts);
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= max)
            return s;
        return s[..max] + "…";
    }

    private static Dictionary<string, Guid> BuildTitleToIdMap(IEnumerable<WorldElement> elements)
    {
        var map = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in elements)
        {
            var t = e.Title.Trim();
            if (string.IsNullOrEmpty(t))
                continue;
            if (!map.ContainsKey(t))
                map[t] = e.Id;
        }

        return map;
    }

    private static List<WorldBuildingSuggestedLink> ResolveSuggestedLinks(
        IReadOnlyList<WorldBuildingLinkDto> suggestedLinks,
        IReadOnlyDictionary<string, Guid> titleToId)
    {
        var result = new List<WorldBuildingSuggestedLink>();
        var seen = new HashSet<(Guid From, Guid To, string Label)>();
        foreach (var link in suggestedLinks)
        {
            if (string.IsNullOrWhiteSpace(link.FromTitle) || string.IsNullOrWhiteSpace(link.ToTitle))
                continue;
            var ft = link.FromTitle.Trim();
            var tt = link.ToTitle.Trim();
            if (!titleToId.TryGetValue(ft, out var fromId))
                continue;
            if (!titleToId.TryGetValue(tt, out var toId))
                continue;
            if (fromId == toId)
                continue;
            var label = HumanizeRelationLabel((link.RelationLabel ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(label))
                continue;
            var key = (fromId, toId, label);
            if (seen.Contains(key))
                continue;
            seen.Add(key);
            result.Add(new WorldBuildingSuggestedLink
            {
                FromWorldElementId = fromId,
                ToWorldElementId = toId,
                FromTitle = ft,
                ToTitle = tt,
                RelationLabel = label
            });
        }

        return result;
    }

    private static WorldElementKind ParseKind(string raw)
    {
        if (Enum.TryParse<WorldElementKind>(raw, ignoreCase: true, out var k))
            return k;
        return WorldElementKind.Other;
    }

    private const int MaxCanonReviewPassChars = 26000;

    public async Task<LinkCanonReviewResult> ReviewLinksCanonAsync(Guid bookId, Guid elementId,
        CancellationToken cancellationToken = default)
    {
        var focus = await _db.WorldElements.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == elementId && w.BookId == bookId, cancellationToken);
        if (focus is null)
            return new LinkCanonReviewResult { Proposals = Array.Empty<LinkCanonReviewProposal>() };

        var allElements = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId)
            .OrderBy(w => w.Title)
            .ToListAsync(cancellationToken);

        var elementIds = allElements.Select(e => e.Id).ToHashSet();
        var allLinks = await _db.WorldElementLinks.AsNoTracking()
            .Where(l => elementIds.Contains(l.FromWorldElementId) && elementIds.Contains(l.ToWorldElementId))
            .ToListAsync(cancellationToken);

        var timelineRows = await _db.TimelineEntries.AsNoTracking()
            .Where(t => t.BookId == bookId)
            .OrderBy(t => t.SortKey)
            .ToListAsync(cancellationToken);

        var titleById = allElements.ToDictionary(e => e.Id, e => e.Title.Trim());
        var linkById = allLinks.ToDictionary(l => l.Id);
        var linkLines = new List<string>();
        foreach (var l in allLinks)
        {
            var ft = titleById.GetValueOrDefault(l.FromWorldElementId) ?? "?";
            var tt = titleById.GetValueOrDefault(l.ToWorldElementId) ?? "?";
            linkLines.Add($"{l.Id} | {ft} | {tt} | {l.RelationLabel.Trim()}");
        }

        var timelineLines = new List<string>();
        foreach (var t in timelineRows)
        {
            var kind = t.Kind == TimelineEntryKind.Scene ? "Scene" : "WorldEvent";
            var weTitle = t.WorldElementId is { } we && titleById.TryGetValue(we, out var wn) ? wn : "—";
            timelineLines.Add(
                $"{t.Id} | {kind} | {Truncate(t.Title, 120)} | {Truncate(t.Summary ?? "", 200)} | linkedWE: {weTitle}");
        }

        var passes = BuildCanonReviewPasses(focus, allElements, linkLines, timelineLines);
        var map = BuildTitleToIdMap(allElements);
        var proposals = new List<LinkCanonReviewProposal>();
        var model = _ollamaOptions.Value.WriterModel;
        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: {
              "proposals": [
                { "kind": "add_link", "fromTitle": string, "toTitle": string, "relationLabel": string, "rationale": string },
                { "kind": "remove_link", "linkId": string (uuid), "rationale": string },
                { "kind": "change_relation", "linkId": string (uuid), "newRelationLabel": string, "rationale": string },
                { "kind": "set_timeline_link", "timelineEntryId": string (uuid), "worldElementTitle": string, "rationale": string }
              ]
            }
            Rules:
            - add_link: both titles must be EXACT matches from ALL WORLD ELEMENTS in the message; any two distinct entries in this book.
            - relationLabel: short plain English (e.g. "Located in", "Leads"). Never snake_case.
            - remove_link / change_relation: linkId must appear under CURRENT LINKS in this message (if that section is present).
            - set_timeline_link: timelineEntryId must appear under TIMELINE ENTRIES in this message (if that section is present). worldElementTitle is an EXACT title from ALL WORLD ELEMENTS or "" to clear.
            - If a section is omitted from this message, do not propose changes that require IDs from that section.
            - Use empty proposals array if nothing should change.
            """;

        foreach (var pass in passes)
        {
            var messages = new List<OllamaChatMessage>
            {
                new("system", system),
                new("user", pass.UserContent)
            };
            var req = JsonSerializer.Serialize(new { model, messages, format = "json" });
            var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options: null, cancellationToken);
            await _db.LlmCalls.AddAsync(new LlmCall
            {
                Id = Guid.NewGuid(),
                GenerationRunId = null,
                BookId = bookId,
                Step = PipelineStep.WorldBuildingLinkCanonReview,
                Model = model,
                RequestJson = req,
                ResponseText = result.MessageText,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var parsed = LlmJson.Deserialize<LinkCanonReviewLlmResult>(result.MessageText) ?? new LinkCanonReviewLlmResult();
            foreach (var p in parsed.Proposals)
            {
                var kind = (p.Kind ?? string.Empty).Trim().ToLowerInvariant();
                var rationale = (p.Rationale ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(rationale))
                    rationale = "—";

                switch (kind)
                {
                    case "add_link":
                    {
                        var prop = TryBuildAddLinkProposal(p, map, rationale);
                        if (prop is not null)
                            proposals.Add(prop);
                        break;
                    }
                    case "remove_link":
                    {
                        if (p.LinkId is not { } lid || !pass.ValidLinkIds.Contains(lid) || !linkById.TryGetValue(lid, out var link))
                            break;
                        proposals.Add(new LinkCanonReviewProposal
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            Kind = "remove_link",
                            Rationale = rationale,
                            LinkId = lid,
                            FromTitle = titleById.GetValueOrDefault(link.FromWorldElementId),
                            ToTitle = titleById.GetValueOrDefault(link.ToWorldElementId),
                            CurrentRelationLabel = link.RelationLabel.Trim()
                        });
                        break;
                    }
                    case "change_relation":
                    {
                        if (p.LinkId is not { } cid || !pass.ValidLinkIds.Contains(cid) || !linkById.TryGetValue(cid, out var clink))
                            break;
                        var nl = HumanizeRelationLabel((p.NewRelationLabel ?? string.Empty).Trim());
                        if (string.IsNullOrEmpty(nl))
                            break;
                        proposals.Add(new LinkCanonReviewProposal
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            Kind = "change_relation",
                            Rationale = rationale,
                            LinkId = cid,
                            FromTitle = titleById.GetValueOrDefault(clink.FromWorldElementId),
                            ToTitle = titleById.GetValueOrDefault(clink.ToWorldElementId),
                            CurrentRelationLabel = clink.RelationLabel.Trim(),
                            NewRelationLabel = nl
                        });
                        break;
                    }
                    case "set_timeline_link":
                    {
                        if (p.TimelineEntryId is not { } teId || !pass.ValidTimelineIds.Contains(teId))
                            break;
                        var te = timelineRows.FirstOrDefault(t => t.Id == teId);
                        if (te is null)
                            break;
                        var currentWe = te.WorldElementId is { } cwe && titleById.TryGetValue(cwe, out var cwt)
                            ? cwt
                            : null;
                        var wet = p.WorldElementTitle?.Trim() ?? string.Empty;
                        if (string.IsNullOrEmpty(wet))
                        {
                            proposals.Add(new LinkCanonReviewProposal
                            {
                                Id = Guid.NewGuid().ToString("D"),
                                Kind = "set_timeline_link",
                                Rationale = rationale,
                                TimelineEntryId = teId,
                                TimelineEntryTitle = te.Title,
                                CurrentWorldElementTitle = currentWe,
                                ClearWorldElementLink = true
                            });
                            break;
                        }

                        if (!map.TryGetValue(wet, out var weGuid))
                            break;
                        proposals.Add(new LinkCanonReviewProposal
                        {
                            Id = Guid.NewGuid().ToString("D"),
                            Kind = "set_timeline_link",
                            Rationale = rationale,
                            TimelineEntryId = teId,
                            TimelineEntryTitle = te.Title,
                            CurrentWorldElementTitle = currentWe,
                            ProposedWorldElementTitle = wet,
                            ProposedWorldElementId = weGuid,
                            ClearWorldElementLink = false
                        });
                        break;
                    }
                }
            }
        }

        return new LinkCanonReviewResult { Proposals = DedupeLinkCanonProposals(proposals) };
    }

    private sealed record CanonReviewPass(string UserContent, HashSet<Guid> ValidLinkIds, HashSet<Guid> ValidTimelineIds);

    private List<CanonReviewPass> BuildCanonReviewPasses(
        WorldElement focus,
        IReadOnlyList<WorldElement> allElements,
        IReadOnlyList<string> linkLines,
        IReadOnlyList<string> timelineLines)
    {
        var footer = """
            Review whether links and timeline↔world-element attachments are consistent with the element summaries.
            Propose add_link for missing obvious relations; remove_link or change_relation for links that contradict the text;
            set_timeline_link to fix wrong or missing timeline↔entry attachments (use "" for worldElementTitle to clear).
            """;

        var baseAndCatalog = BuildCanonReviewBaseCatalog(focus, allElements);
        var single = new StringBuilder();
        single.Append(baseAndCatalog);
        single.AppendLine("CURRENT LINKS (all; linkId | from | to | relation):");
        if (linkLines.Count == 0)
            single.AppendLine("(none)");
        else
            foreach (var line in linkLines)
                single.AppendLine(line);
        single.AppendLine();
        single.AppendLine("TIMELINE ENTRIES (all; id | kind | title | summary | linked world element):");
        if (timelineLines.Count == 0)
            single.AppendLine("(none — do not invent ids)");
        else
            foreach (var line in timelineLines)
                single.AppendLine(line);
        single.AppendLine();
        single.AppendLine(footer);

        if (single.Length <= MaxCanonReviewPassChars)
        {
            var allLinkIds = new HashSet<Guid>(linkLines.Select(CanonLineLeadingGuid).Where(g => g.HasValue).Select(g => g!.Value));
            var allTlIds = new HashSet<Guid>(timelineLines.Select(CanonLineLeadingGuid).Where(g => g.HasValue).Select(g => g!.Value));
            return new List<CanonReviewPass> { new(single.ToString(), allLinkIds, allTlIds) };
        }

        var passes = new List<CanonReviewPass>();
        var linkHeader = "CURRENT LINKS (only linkIds listed here may be removed or re-labeled):\n";
        var timelineHeader = "TIMELINE ENTRIES (only timelineEntryIds listed here may use set_timeline_link):\n";
        var linkPassOverhead = baseAndCatalog.Length + footer.Length + linkHeader.Length + 120;
        var linkBudget = Math.Max(1500, MaxCanonReviewPassChars - linkPassOverhead);
        var tlPassOverhead = baseAndCatalog.Length + footer.Length + timelineHeader.Length + 120;
        var tlBudget = Math.Max(1500, MaxCanonReviewPassChars - tlPassOverhead);

        foreach (var batch in SplitLinesByCharBudget(linkLines, linkBudget))
        {
            var sb = new StringBuilder();
            sb.Append(baseAndCatalog);
            sb.Append(linkHeader);
            if (batch.Count == 0)
                sb.AppendLine("(none)");
            else
                foreach (var line in batch)
                    sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine("(Timeline rows are in separate passes; do not propose set_timeline_link here.)");
            sb.AppendLine();
            sb.AppendLine(footer);
            var ids = new HashSet<Guid>(batch.Select(CanonLineLeadingGuid).Where(g => g.HasValue).Select(g => g!.Value));
            passes.Add(new CanonReviewPass(sb.ToString(), ids, new HashSet<Guid>()));
        }

        foreach (var batch in SplitLinesByCharBudget(timelineLines, tlBudget))
        {
            var sb = new StringBuilder();
            sb.Append(baseAndCatalog);
            sb.AppendLine(
                "(remove_link / change_relation use other passes; you may still propose add_link using ALL WORLD ELEMENTS above.)");
            sb.AppendLine();
            sb.Append(timelineHeader);
            if (batch.Count == 0)
                sb.AppendLine("(none)");
            else
                foreach (var line in batch)
                    sb.AppendLine(line);
            sb.AppendLine();
            sb.AppendLine(footer);
            var ids = new HashSet<Guid>(batch.Select(CanonLineLeadingGuid).Where(g => g.HasValue).Select(g => g!.Value));
            passes.Add(new CanonReviewPass(sb.ToString(), new HashSet<Guid>(), ids));
        }

        if (passes.Count == 0)
        {
            passes.Add(new CanonReviewPass(
                baseAndCatalog + "\n" + footer,
                new HashSet<Guid>(),
                new HashSet<Guid>()));
        }

        return passes;
    }

    private string BuildCanonReviewBaseCatalog(WorldElement focus, IReadOnlyList<WorldElement> allElements)
    {
        const int maxBaseChars = 22000;
        foreach (var cap in new[] { 400, 280, 180, 120, 80 })
        {
            var sb = new StringBuilder();
            sb.AppendLine("CONTEXT (started from this row in the UI)");
            sb.AppendLine($"Focused element Id: {focus.Id}");
            sb.AppendLine($"Title: {focus.Title}");
            sb.AppendLine($"Kind: {focus.Kind}");
            sb.AppendLine($"Summary: {Truncate(focus.Summary, Math.Min(1200, cap * 3))}");
            sb.AppendLine($"Detail: {Truncate(focus.Detail, Math.Min(2500, cap * 6))}");
            sb.AppendLine();
            sb.AppendLine("ALL WORLD ELEMENTS in this book (use EXACT titles for add_link and set_timeline_link):");
            foreach (var e in allElements)
            {
                sb.AppendLine(
                    $"- {e.Title} | {e.Kind} | {Truncate(e.Summary, cap)}");
            }

            sb.AppendLine();
            if (sb.Length <= maxBaseChars)
                return sb.ToString();
        }

        var fallback = new StringBuilder();
        fallback.AppendLine("CONTEXT");
        fallback.AppendLine($"Focused: {focus.Title} | {focus.Kind} | {Truncate(focus.Summary, 200)}");
        fallback.AppendLine();
        fallback.AppendLine("ALL WORLD ELEMENTS (titles — use EXACT):");
        foreach (var e in allElements)
            fallback.AppendLine($"- {e.Title} | {e.Kind}");
        fallback.AppendLine();
        return fallback.ToString();
    }

    private static Guid? CanonLineLeadingGuid(string line)
    {
        var idx = line.IndexOf('|', StringComparison.Ordinal);
        if (idx <= 0)
            return null;
        var prefix = line[..idx].Trim();
        return Guid.TryParse(prefix, out var g) ? g : null;
    }

    private static List<List<string>> SplitLinesByCharBudget(IReadOnlyList<string> lines, int maxCharsPerBatch)
    {
        var batches = new List<List<string>>();
        if (lines.Count == 0)
            return batches;
        var cur = new List<string>();
        var len = 0;
        foreach (var line in lines)
        {
            var add = line.Length + (cur.Count > 0 ? 1 : 0);
            if (len + add > maxCharsPerBatch && cur.Count > 0)
            {
                batches.Add(cur);
                cur = new List<string>();
                len = 0;
            }

            cur.Add(line);
            len += add;
        }

        if (cur.Count > 0)
            batches.Add(cur);
        return batches;
    }

    private static LinkCanonReviewProposal? TryBuildAddLinkProposal(LinkCanonReviewLlmProposal p,
        IReadOnlyDictionary<string, Guid> titleToId, string rationale)
    {
        var ft = (p.FromTitle ?? string.Empty).Trim();
        var tt = (p.ToTitle ?? string.Empty).Trim();
        var rel = HumanizeRelationLabel((p.RelationLabel ?? string.Empty).Trim());
        if (string.IsNullOrEmpty(ft) || string.IsNullOrEmpty(tt) || string.IsNullOrEmpty(rel))
            return null;
        if (!titleToId.TryGetValue(ft, out var fromId) || !titleToId.TryGetValue(tt, out var toId))
            return null;
        if (fromId == toId)
            return null;
        return new LinkCanonReviewProposal
        {
            Id = Guid.NewGuid().ToString("D"),
            Kind = "add_link",
            Rationale = rationale,
            FromTitle = ft,
            ToTitle = tt,
            RelationLabel = rel,
            FromWorldElementId = fromId,
            ToWorldElementId = toId
        };
    }

    private static List<LinkCanonReviewProposal> DedupeLinkCanonProposals(List<LinkCanonReviewProposal> proposals)
    {
        var seen = new HashSet<string>();
        var outList = new List<LinkCanonReviewProposal>();
        foreach (var pr in proposals)
        {
            string key = pr.Kind switch
            {
                "add_link" when pr.FromWorldElementId is { } a && pr.ToWorldElementId is { } b =>
                    $"add:{a:D}:{b:D}:{pr.RelationLabel}",
                "remove_link" when pr.LinkId is { } lid => $"rm:{lid:D}",
                "change_relation" when pr.LinkId is { } cid && pr.NewRelationLabel is { } nl => $"ch:{cid:D}:{nl}",
                "set_timeline_link" when pr.TimelineEntryId is { } te =>
                    pr.ClearWorldElementLink ? $"tl:{te:D}:clear" : $"tl:{te:D}:{pr.ProposedWorldElementId:D}",
                _ => pr.Id
            };
            if (seen.Add(key))
                outList.Add(pr);
        }

        return outList;
    }

    public async Task<LinkCanonApplyResult> ApplyLinkCanonReviewAsync(Guid bookId, IReadOnlyList<ApplyLinkCanonItem> items,
        CancellationToken cancellationToken = default)
    {
        var r = new LinkCanonApplyResult();
        if (items.Count == 0)
            return r;

        var promoteDraftToCanon = new HashSet<Guid>();

        var removes = items.Where(i => string.Equals(i.Kind, "remove_link", StringComparison.OrdinalIgnoreCase)).ToList();
        var changes = items.Where(i => string.Equals(i.Kind, "change_relation", StringComparison.OrdinalIgnoreCase)).ToList();
        var adds = items.Where(i => string.Equals(i.Kind, "add_link", StringComparison.OrdinalIgnoreCase)).ToList();
        var timelines = items.Where(i => string.Equals(i.Kind, "set_timeline_link", StringComparison.OrdinalIgnoreCase)).ToList();

        foreach (var it in removes)
        {
            if (it.LinkId is not { } lid)
                continue;
            var link = await _db.WorldElementLinks
                .Include(x => x.FromWorldElement)
                .Include(x => x.ToWorldElement)
                .FirstOrDefaultAsync(x => x.Id == lid, cancellationToken);
            if (link is null || link.FromWorldElement.BookId != bookId || link.ToWorldElement.BookId != bookId)
                continue;
            promoteDraftToCanon.Add(link.FromWorldElementId);
            promoteDraftToCanon.Add(link.ToWorldElementId);
            _db.WorldElementLinks.Remove(link);
            await _db.SaveChangesAsync(cancellationToken);
            r.LinksRemoved++;
        }

        foreach (var it in changes)
        {
            if (it.LinkId is not { } cid)
                continue;
            var nl = HumanizeRelationLabel((it.NewRelationLabel ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(nl))
                continue;
            var link = await _db.WorldElementLinks
                .Include(x => x.FromWorldElement)
                .Include(x => x.ToWorldElement)
                .FirstOrDefaultAsync(x => x.Id == cid, cancellationToken);
            if (link is null || link.FromWorldElement.BookId != bookId || link.ToWorldElement.BookId != bookId)
                continue;
            promoteDraftToCanon.Add(link.FromWorldElementId);
            promoteDraftToCanon.Add(link.ToWorldElementId);
            link.RelationLabel = nl;
            await _db.SaveChangesAsync(cancellationToken);
            r.RelationsUpdated++;
        }

        foreach (var it in adds)
        {
            if (it.FromWorldElementId is not { } fromId || it.ToWorldElementId is not { } toId || fromId == toId)
                continue;
            var label = HumanizeRelationLabel((it.RelationLabel ?? string.Empty).Trim());
            if (string.IsNullOrEmpty(label))
                continue;
            var fromOk = await _db.WorldElements.AsNoTracking()
                .AnyAsync(w => w.Id == fromId && w.BookId == bookId, cancellationToken);
            var toOk = await _db.WorldElements.AsNoTracking()
                .AnyAsync(w => w.Id == toId && w.BookId == bookId, cancellationToken);
            if (!fromOk || !toOk)
                continue;
            _db.WorldElementLinks.Add(new WorldElementLink
            {
                Id = Guid.NewGuid(),
                FromWorldElementId = fromId,
                ToWorldElementId = toId,
                RelationLabel = label
            });
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                promoteDraftToCanon.Add(fromId);
                promoteDraftToCanon.Add(toId);
                r.LinksAdded++;
            }
            catch (DbUpdateException)
            {
                // duplicate
            }
        }

        foreach (var it in timelines)
        {
            if (it.TimelineEntryId is not { } teId)
                continue;
            var entry = await _db.TimelineEntries.FirstOrDefaultAsync(t => t.Id == teId && t.BookId == bookId,
                cancellationToken);
            if (entry is null)
                continue;
            if (it.ClearWorldElementId)
            {
                if (entry.WorldElementId is { } prevWe)
                    promoteDraftToCanon.Add(prevWe);
                entry.WorldElementId = null;
                await _db.SaveChangesAsync(cancellationToken);
                r.TimelineEntriesUpdated++;
                continue;
            }

            if (it.WorldElementId is { } weId)
            {
                var weOk = await _db.WorldElements.AsNoTracking()
                    .AnyAsync(w => w.Id == weId && w.BookId == bookId, cancellationToken);
                if (!weOk)
                    continue;
                promoteDraftToCanon.Add(weId);
                entry.WorldElementId = weId;
                await _db.SaveChangesAsync(cancellationToken);
                r.TimelineEntriesUpdated++;
            }
        }

        if (promoteDraftToCanon.Count > 0)
        {
            var now = DateTimeOffset.UtcNow;
            var toPromote = await _db.WorldElements
                .Where(w => w.BookId == bookId && promoteDraftToCanon.Contains(w.Id) && w.Status == WorldElementStatus.Draft)
                .ToListAsync(cancellationToken);
            foreach (var w in toPromote)
            {
                w.Status = WorldElementStatus.Canon;
                w.UpdatedAt = now;
            }

            if (toPromote.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);
        }

        return r;
    }

    public async Task<IReadOnlyList<Guid>> SuggestWorldElementsForSynopsisAsync(Guid bookId, string synopsis,
        CancellationToken cancellationToken = default)
    {
        var t = synopsis.Trim();
        if (string.IsNullOrEmpty(t))
            return Array.Empty<Guid>();

        await _db.Books.AsNoTracking().FirstAsync(b => b.Id == bookId, cancellationToken);
        var elements = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId)
            .OrderBy(w => w.Title)
            .Select(w => new { w.Id, w.Kind, w.Title, w.Summary })
            .ToListAsync(cancellationToken);
        if (elements.Count == 0)
            return Array.Empty<Guid>();

        var model = _ollamaOptions.Value.WriterModel;
        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: { "elementIds": string[] } — each value must be a UUID copied exactly from the input list.
            Given a scene synopsis, pick world-building entries that would meaningfully appear or be referenced (places, people, factions, objects, lore).
            Order by relevance. Return at most 24 ids. Return an empty array if none fit.
            """;
        var user = new StringBuilder();
        user.AppendLine("Scene synopsis:");
        user.AppendLine(t);
        user.AppendLine();
        user.AppendLine("World elements (id | kind | title | summary):");
        foreach (var e in elements)
            user.AppendLine($"- {e.Id} | {e.Kind} | {e.Title} | {Truncate(e.Summary, 200)}");

        var messages = new List<OllamaChatMessage> { new("system", system), new("user", user.ToString()) };
        var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options: null, cancellationToken);
        await _db.LlmCalls.AddAsync(new LlmCall
        {
            Id = Guid.NewGuid(),
            GenerationRunId = null,
            BookId = bookId,
            Step = PipelineStep.SceneSynopsisWorldElements,
            Model = model,
            RequestJson = JsonSerializer.Serialize(new { model, messages, format = "json" }),
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var parsed = LlmJson.Deserialize<SceneSynopsisWorldElementsLlmResult>(result.MessageText) ??
                     new SceneSynopsisWorldElementsLlmResult();
        var valid = elements.Select(x => x.Id).ToHashSet();
        var outList = new List<Guid>();
        foreach (var s in parsed.ElementIds)
        {
            if (Guid.TryParse(s, out var id) && valid.Contains(id) && !outList.Contains(id))
                outList.Add(id);
        }

        return outList;
    }

    private static readonly Regex GlossaryLeadingArticleRegex =
        new(@"^(the|a|an)\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<string?> BuildGlossaryMarkdownAsync(Guid bookId, bool useLlmForAlternateNames,
        CancellationToken cancellationToken = default)
    {
        var book = await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookId, cancellationToken);
        if (book is null)
            return null;

        var elements = await _db.WorldElements.AsNoTracking()
            .Where(w => w.BookId == bookId)
            .ToListAsync(cancellationToken);

        var alternatesById = new Dictionary<Guid, HashSet<string>>();
        foreach (var e in elements)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddMetadataAlternates(e.MetadataJson, set);
            AddSlugAlternate(e.Title, e.Slug, set);
            AddLastNameFirstAlternateForCharacter(e.Kind, e.Title, set);
            alternatesById[e.Id] = set;
        }

        if (useLlmForAlternateNames && elements.Count > 0)
            await MergeGlossaryLlmAlternatesAsync(bookId, elements, alternatesById, cancellationToken);

        foreach (var e in elements)
        {
            if (alternatesById.TryGetValue(e.Id, out var set))
                set.Remove(e.Title.Trim());
        }

        var primaryTitles = new HashSet<string>(
            elements.Select(e => e.Title.Trim()),
            StringComparer.OrdinalIgnoreCase);

        var glossaryItems = BuildGlossaryMarkdownItems(elements, alternatesById, primaryTitles);

        var sb = new StringBuilder();
        sb.AppendLine($"# Glossary — {GlossaryEscapeLine(book.Title)}");
        sb.AppendLine();
        var totalRows = glossaryItems.Count;
        sb.AppendLine(
            $"_Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC · {totalRows} entr{(totalRows == 1 ? "y" : "ies")}_");
        sb.AppendLine();

        var grouped = glossaryItems
            .GroupBy(i => SectionLetterKey(i.SortTitle))
            .OrderBy(g => SectionOrder(g.Key));
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var item in group.OrderBy(i => SortKeyForGlossary(i.SortTitle), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(i => i.SortTitle, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(i => i.IsPrimary ? 0 : 1))
            {
                if (item.IsPrimary)
                {
                    var e = item.PrimaryElement!;
                    sb.AppendLine($"### {GlossaryEscapeLine(e.Title)}");
                    sb.AppendLine($"- **Kind:** {e.Kind}");
                    sb.AppendLine($"- **Status:** {e.Status}");
                    if (!string.IsNullOrWhiteSpace(e.Summary))
                        sb.AppendLine($"- **Summary:** {GlossaryEscapeLine(e.Summary.Trim())}");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"### {GlossaryEscapeLine(item.StubTitle!)}");
                    foreach (var target in item.SeeTargets!)
                        sb.AppendLine($"- _(See: {GlossaryEscapeLine(target)})_");
                    sb.AppendLine();
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private sealed class GlossaryMarkdownItem
    {
        public required string SortTitle { get; init; }
        public bool IsPrimary { get; init; }
        public WorldElement? PrimaryElement { get; init; }
        public string? StubTitle { get; init; }
        public IReadOnlyList<string>? SeeTargets { get; init; }
    }

    private static List<GlossaryMarkdownItem> BuildGlossaryMarkdownItems(
        IReadOnlyList<WorldElement> elements,
        Dictionary<Guid, HashSet<string>> alternatesById,
        HashSet<string> primaryTitles)
    {
        var items = new List<GlossaryMarkdownItem>();
        foreach (var e in elements)
        {
            items.Add(new GlossaryMarkdownItem
            {
                SortTitle = e.Title,
                IsPrimary = true,
                PrimaryElement = e
            });
        }

        var stubMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in elements)
        {
            if (!alternatesById.TryGetValue(e.Id, out var alts) || alts.Count == 0)
                continue;
            var primary = e.Title.Trim();
            foreach (var a in alts)
            {
                var aTrim = a.Trim();
                if (string.IsNullOrEmpty(aTrim))
                    continue;
                if (primaryTitles.Contains(aTrim))
                    continue;
                if (!stubMap.TryGetValue(aTrim, out var list))
                {
                    list = new List<string>();
                    stubMap[aTrim] = list;
                }

                if (!list.Contains(primary, StringComparer.OrdinalIgnoreCase))
                    list.Add(primary);
            }
        }

        foreach (var kv in stubMap)
        {
            kv.Value.Sort(StringComparer.OrdinalIgnoreCase);
            items.Add(new GlossaryMarkdownItem
            {
                SortTitle = kv.Key,
                IsPrimary = false,
                StubTitle = kv.Key,
                SeeTargets = kv.Value
            });
        }

        return items;
    }

    private static void AddLastNameFirstAlternateForCharacter(WorldElementKind kind, string title, HashSet<string> set)
    {
        if (kind != WorldElementKind.Character)
            return;
        var inv = TryFormatLastNameCommaFirst(title);
        if (inv is not null)
            set.Add(inv);
    }

    /// <summary>"Mary Jane Watson" → "Watson, Mary Jane"; "John Smith" → "Smith, John". Single-word names skipped.</summary>
    private static string? TryFormatLastNameCommaFirst(string title)
    {
        var t = title.Trim();
        if (string.IsNullOrEmpty(t))
            return null;
        var parts = t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        var last = parts[^1];
        var first = string.Join(" ", parts[0..^1]);
        var result = $"{last}, {first}";
        if (string.Equals(result, t, StringComparison.OrdinalIgnoreCase))
            return null;
        return result;
    }

    private static void AddMetadataAlternates(string? json, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;
        try
        {
            var m = JsonSerializer.Deserialize<WorldElementMetadataGlossary>(json);
            if (m?.AlternateNames is null)
                return;
            foreach (var a in m.AlternateNames)
            {
                var t = a?.Trim();
                if (!string.IsNullOrEmpty(t))
                    set.Add(t);
            }
        }
        catch (JsonException)
        {
            /* ignore malformed metadata */
        }
    }

    private static void AddSlugAlternate(string title, string? slug, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return;
        var human = slug.Replace('-', ' ').Replace('_', ' ').Trim();
        if (human.Length == 0)
            return;
        if (string.Equals(human, title.Trim(), StringComparison.OrdinalIgnoreCase))
            return;
        set.Add(human);
    }

    private async Task MergeGlossaryLlmAlternatesAsync(Guid bookId, IReadOnlyList<WorldElement> elements,
        Dictionary<Guid, HashSet<string>> alternatesById, CancellationToken cancellationToken)
    {
        const int chunkSize = 40;
        var model = _ollamaOptions.Value.WriterModel;
        var system = """
            You output ONLY valid JSON. No markdown fences.
            Schema: { "entries": [ { "elementId": string (uuid), "alternateNames": string[] } ] }.
            For each listed world-building entry, provide 0–8 alternate names readers might use: nicknames, epithets, shortened or regional names, or common references.
            Do NOT repeat the primary title. For Character entries, do NOT output "LastName, FirstName" (that form is added automatically for lookup). Use EXACT elementId values from the input. Use an empty array when there are no good alternates.
            Do not invent plot facts; only names supported by the title and summary.
            """;

        for (var i = 0; i < elements.Count; i += chunkSize)
        {
            var chunk = elements.Skip(i).Take(chunkSize).ToList();
            var chunkIds = chunk.Select(x => x.Id).ToHashSet();
            var user = new StringBuilder();
            foreach (var e in chunk)
            {
                user.AppendLine($"- elementId: {e.Id}");
                user.AppendLine($"  title: {e.Title}");
                user.AppendLine($"  kind: {e.Kind}");
                user.AppendLine($"  summary: {Truncate(e.Summary, 450)}");
                user.AppendLine();
            }

            var messages = new List<OllamaChatMessage>
            {
                new("system", system),
                new("user", user.ToString())
            };
            var req = JsonSerializer.Serialize(new { model, messages, format = "json" });
            var result = await _ollama.ChatAsync(model, messages, jsonFormat: true, options: null, cancellationToken);
            await _db.LlmCalls.AddAsync(new LlmCall
            {
                Id = Guid.NewGuid(),
                GenerationRunId = null,
                BookId = bookId,
                Step = PipelineStep.WorldBuildingGlossary,
                Model = model,
                RequestJson = req,
                ResponseText = result.MessageText,
                CreatedAt = DateTimeOffset.UtcNow
            }, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            var parsed = LlmJson.Deserialize<GlossaryAlternateLlmResult>(result.MessageText) ?? new GlossaryAlternateLlmResult();
            foreach (var row in parsed.Entries)
            {
                if (!chunkIds.Contains(row.ElementId))
                    continue;
                if (!alternatesById.TryGetValue(row.ElementId, out var set))
                    continue;
                foreach (var a in row.AlternateNames)
                {
                    var t = a?.Trim();
                    if (!string.IsNullOrEmpty(t))
                        set.Add(t);
                }
            }
        }
    }

    private static string SortKeyForGlossary(string title) => StripLeadingArticlesForGlossary(title);

    private static string StripLeadingArticlesForGlossary(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return string.Empty;
        return GlossaryLeadingArticleRegex.Replace(s.Trim(), "").Trim();
    }

    private static string SectionLetterKey(string title)
    {
        var key = StripLeadingArticlesForGlossary(title);
        if (string.IsNullOrEmpty(key))
            return "Other";
        var c = char.ToUpperInvariant(key[0]);
        if (c >= 'A' && c <= 'Z')
            return c.ToString();
        return "Other";
    }

    private static int SectionOrder(string key)
    {
        if (key == "Other")
            return 26;
        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
            return key[0] - 'A';
        return 27;
    }

    private static string GlossaryEscapeLine(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        return s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
    }
}
