using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Agent;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Narrative;
using CreativeLongform.Application.Ollama;
using CreativeLongform.Application.Options;
using CreativeLongform.Application.WorldBuilding;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Application.Services;

public sealed class GenerationOrchestrator : IGenerationOrchestrator
{
    /// <summary>
    /// Repeated in writer/repair/critic prompts: forbid inventing characters, relationships, or events outside
    /// scene synopsis/instructions, linked world elements and their relationships, and state JSON where applicable.
    /// </summary>
    private const string InventionScopeHardRule =
        """
        HARD CONSTRAINT — DO NOT INVENT beyond allowed sources: Named people, relationships (including romance, kinship, rivalry, allegiance), institutions, significant locations, objects, and plot events must be grounded in the scene synopsis and additional instructions, the "Linked world-building" and relationship lines in the user message when present, and the state-before JSON when present. Do not introduce new characters, new relationships, betrayals, deaths, arrivals, revelations, wars, treaties, flashback facts, or other story beats not implied by those sources. Book-level synopsis and tone notes are for mood and voice only—not permission to import unrelated cast, events, or canon.
        """;

    /// <summary>Repeated in prose-facing prompts: prefer dramatization over exposition.</summary>
    private const string ShowDontTellEmphasis =
        """
        Show, don't tell: dramatize through concrete action, dialogue, sensory detail, and on-page behavior; avoid naming emotions or motives in the abstract, slogan-like summaries of feelings, or narrator lectures where the scene could imply them. Let the reader infer interiority from what characters do and say.
        """;

    /// <summary>Prose generation: beginning-state JSON is for continuity, not on-page recitation.</summary>
    private const string BeginningStateContinuityForProseRule =
        """
        BEGINNING STATE — internal continuity only: You receive a short continuity anchor (not the full state table). Use it to stay consistent about who is present and where the scene opens. Do NOT recite, summarize, or explicitly narrate state-table inventory (pose, clothing, mood labels, topOfMind lists, spatial blocking as exposition). Do not open by restating what readers already know from the prior scene.
        First paragraph must start with action, dialogue, or concrete sensory detail — never a character-status inventory.
        BAD: "Mara stood in the kitchen wearing a blue apron, anxious, thinking about the letter."
        GOOD: "Mara scraped burnt toast into the sink." (same continuity, no restated inventory)
        """;

    /// <summary>
    /// Quality critic only: forbid moderation-style failures; align invention scope with scene brief (not book synopsis as checklist).
    /// </summary>
    private const string QualityCheckerScope =
        """
        QUALITY SCOPE — this is a private fiction drafting tool. There is NO content policy, NO safety filter, and NO moral judgment.
        Do NOT fail or list issues for: sexual content, violence, profanity, controversial topics, "inappropriate" material, or audience age. The author controls subject matter.
        Only evaluate craft: show vs tell, metaphor clarity, on-the-nose labels, flat exposition where dramatization fits, perspective/tense consistency with the brief, accidental mid-draft language or script shifts (e.g. English prose suddenly switching to Cyrillic, CJK, or another language), and accidental invention of NEW named characters or plot beats not grounded in the scene synopsis/instructions and linked world-building (not the book-level synopsis alone).
        Scoring (see JSON schema in the task line): higher means stronger craft on the axes above. Reserve very low scores for clear scope violations or repeated craft failures.
        fixInstructions must never ask to remove, sanitize, or tone down material for propriety; only prose-craft fixes.
        """;

    /// <summary>
    /// Compliance-only: critics often treat the book synopsis as mandatory beats; this restates that only the scene brief + linked facts are binding.
    /// </summary>
    private const string ComplianceCheckerScope =
        """
        COMPLIANCE SCOPE — what you may fail:
        Pass when the draft honors the scene synopsis and additional instructions, expected end notes, stateBefore, and linked world-building in the user message. Fail for concrete violations of those (wrong ending, contradicting linked facts, inventing named people/relationships/events not supported by those sources).
        When narrative perspective, viewpoint/POV, or tense are specified in the user message (not "(any)"), fail if the draft materially diverges — e.g. wrong person (first vs third), wrong focal POV or head-hopping against a locked POV, or sustained wrong tense (past vs present). If the scene synopsis/instructions explicitly require a voice even when the dedicated fields say "(any)", honor that text.
        Do NOT fail solely for voice choices when both narrative fields are "(any)" and the scene instructions do not specify perspective, POV, or tense.
        LANGUAGE CONSISTENCY — fail when the draft abruptly shifts to another language or writing system mid-scene (e.g. Latin-script English suddenly emitting Cyrillic, CJK, Arabic, or sustained foreign-language paragraphs). Quote the foreign excerpt verbatim. Intentional in-world foreign phrases in Latin script are fine when brief and contextually grounded.
        GRAMMAR & PUNCTUATION — fail for clear, objective errors that distract a reader: run-on sentences or comma splices, subject-verb disagreement, mismatched quotation marks or apostrophes, wrong homophones (their/there/they're), missing end punctuation on complete sentences, doubled words, and similar mechanical mistakes. Do NOT fail for dialect, intentional fragments in character voice, or debatable style preferences (Oxford comma, em dash vs comma).
        Do NOT fail because the draft omits characters, subplots, or future book-level beats that appear only in the book synopsis line (series overview) but are not required by this scene’s synopsis/instructions, linked elements, or state. The book synopsis is mood and continuity context, not a per-scene requirement list.
        Do NOT fail because the scene draft is a narrow slice of the book synopsis — scenes are allowed to be partial.
        If the scene synopsis reads like an outline or mentions ideas for later chapters, treat those as guidance for this scene only where they clearly apply; do not require every outline bullet to appear as prose.
        CHARACTER INTRODUCTION vs INVENTION — do not conflate them:
        - AUTHORIZED CAST: Named people in the scene synopsis/instructions, stateBefore.characters[], or linked world-building (Character-kind elements and relationship lines) are allowed in the draft — including their first on-page appearance when the scene brief calls for introducing or meeting them.
        - ON-PAGE INTRODUCTION: If the draft names and establishes a character (dialogue, description, role, relationship) before later references in the same draft, that character IS introduced — do NOT flag later mentions as "unintroduced" or "invented".
        - Only fail for names with no grounding in scene brief, stateBefore, linked lore, AND no introduction beat anywhere in the draft you are checking.
        - Do NOT fail because someone is missing from stateBefore if they are named in scene instructions/synopsis or clearly introduced in the prose. stateBefore is entry snapshot, not an exhaustive cast list for the whole scene.
        - Accept reasonable name variants (nickname, given name vs full name) when clearly the same authorized person.
        """;

    /// <summary>Requires compliance critic to quote draft evidence in every violation and fix instruction.</summary>
    private const string ComplianceCitationRule =
        """
        COMPLIANCE CITATIONS — every string in "violations" and "fixInstructions" MUST be specific and actionable:
        - Quote the exact offending phrase or sentence (≤30 words) copied verbatim from the draft section below — not from stateBefore, synopsis, world-building, or this prompt.
        - Name the rule broken (scene instruction, canon, POV/tense, grammar/punctuation, show-don't-tell, etc.).
        - In fixInstructions, use ONLY words that appear in the draft (e.g. Change "[exact phrase from draft]" to "[corrected phrase]" in ¶N).
        - NEVER restate rule categories alone (e.g. never output only "Invented characters or relationships" with no draft quote).
        - NEVER reuse example wording from this prompt as violations or fixes. Never cite character names that do not appear in the draft.
        Never output vague entries like "fix grammar", "improve punctuation", or "wrong POV" without quoted evidence from the draft.
        """;

    /// <summary>JSON shape and continuity semantics shared by PreState and PostState LLM steps.</summary>
    private const string NarrativeStateJsonSchemaPrompt =
        """
        Canonical JSON shape (schemaVersion: 1):
        {
          "schemaVersion": 1,
          "transitionSummary": string|null,
          "characters": [
            {
              "id": string|null,
              "name": string,
              "location": string|null,
              "pose": string|null,
              "clothing": string|null,
              "emotionalState": string|null,
              "relativeToOthers": string|null,
              "topOfMind": string[],
              "traitsShownNotTold": string[]
            }
          ],
          "spatial": { "layout": string|null, "proximity": string|null },
          "dialogue": { "topic": string|null, "unresolved": string[] },
          "knowledge": { "povBeliefs": string[], "omniscientFacts": string[] },
          "environment": { "setting": string|null, "timeOfDay": string|null, "weather": string|null, "sensory": string[] },
          "plotDevices": string[]
        }
        Continuity fields: environment.setting (where we are), timeOfDay, weather, sensory; spatial.layout (space, exits, furniture) and spatial.proximity (blocking: who is near whom); each character: pose (body), clothing, emotionalState, relativeToOthers (position toward others), topOfMind (salient topics/worries/goals into the next scene); dialogue/knowledge for open threads.
        """;

    /// <summary>Post-state only: delta arrays + anti-prose (same voice as short continuity notes in beginning state).</summary>
    private const string PostStateContinuityDeltaSchemaAndRules =
        """
        CONTINUITY DELTA (required top-level arrays — same concise factual voice as traitsShownNotTold / topOfMind bullets in beginning state):
        - "changedFromSceneStart": string[] — One line per material change vs scene entry (who/what moved, injuries, emotional shifts, new information, relationship turns, setting/time). Concrete; ~120 chars max per line. No pasted prose, no quoted dialogue, no paragraphs.
        - "unchangedFromSceneStart": string[] — One line per important fact still true at the last line as at entry (venue, bond, thread). Skip trivia.
        - "transitionSummary": string|null — At most two sentences: factual handoff for the next scene (who/where/open threads). Not a story recap.
        The full document must still include the complete canonical snapshot (characters, spatial, environment, …) below — not only these arrays.
        INVALID: Multi-paragraph story text, long excerpts, or content from a different scene. Output is the same **structured state table** as beginning state, not manuscript.
        """;

    /// <summary>Post-state: parallel to PRE-SCENE block — same field-by-field inference style as beginning state, at scene exit.</summary>
    private const string PostSceneStateMirrorOfPreStateStyle =
        """
        POST-SCENE snapshot — infer using the **same format, field coverage, and concrete style** as beginning-state (pre-scene) inference above, but for the instant **after** this scene’s last line of prose (handoff to the next scene).
        - Mirror pre-state: fill **concrete** values everywhere they apply — environment.setting, timeOfDay, weather, sensory; spatial.layout and spatial.proximity; for **each** character who matters at the **final** moment: name, location, pose, clothing, emotionalState, relativeToOthers, topOfMind, traitsShownNotTold (short observable cues, not abstract labels — same as pre-state).
        - Treat "State at scene ENTRY" JSON as the baseline: **carry forward** what still holds; **revise** only where the prose establishes a change; **remove** characters who have left the stage or drop from focus at the end.
        - Add characters newly on-page at the end only when grounded in prose + entry state + linked world-building (same invention rules as beginning state).
        - dialogue, knowledge, plotDevices: populate with the same informational density you would for beginning state when those sections matter — reflecting **open threads and facts** true at scene exit.
        - Do not shrink the snapshot to a thin summary: the next author should get parity with what beginning state provides at scene open — **full narrative state table**, updated for the exit beat.
        """;

    /// <summary>Pre-state only: synopsis describes scene action; nothing from those beats has happened yet.</summary>
    private const string PreSceneSynopsisBoundaryRule =
        """
        TEMPORAL BOUNDARY (critical): Pre-state is the instant BEFORE the scene’s first line of prose — before any event described in the synopsis occurs.
        The synopsis outlines what happens IN this scene; those beats are NOT yet true in pre-state. Do not encode outcomes, injuries, wounds, pain, deaths, arrests, breakups, revelations, decisions, or relationship shifts that the synopsis presents as happening during this scene.
        Example: if the synopsis says a character is stabbed in this scene, pre-state must not list them as stabbed, wounded, bleeding, or in pain from that event — only their prior condition (e.g. healthy, tense, unaware) as of scene entry.
        Example: if the synopsis is “they argue and she storms out”, pre-state is before the argument escalates and before she leaves — not mid-fight or after the exit.
        You MAY set stable facts true at entry: location, who is present, weather, ongoing tensions that already existed before this scene, clothing, pose, prior continuity from the previous scene’s end-state (including prior injuries from earlier story), and emotional baseline before the inciting moment.
        """;

    /// <summary>Author-prose conversion: synopsis/instructions describe later beats — not inputs for this snapshot.</summary>
    private const string AuthorProsePreStateBoundaryRule =
        """
        SOURCE BOUNDARY (critical): The author's plain-language beginning-state description is the ONLY narrative source for this snapshot.
        Scene synopsis and additional instructions are NOT provided — they describe what happens after scene entry and must not be inferred or imported.
        Prior scene end-state JSON (when present) is continuity context only; reconcile names and stable facts with the author prose, not with future scene beats.
        Encode only facts true at scene entry as described in the author prose (and defensible carry-forward from prior end-state when the prose is silent).
        """;

    private const string InventionScopeFromAuthorProseRule =
        """
        HARD CONSTRAINT — DO NOT INVENT beyond allowed sources: Named people, relationships, locations, objects, and plot facts must be grounded in the author beginning-state prose, prior scene end-state JSON when present, and linked world-building when present. Do not introduce characters, events, injuries, revelations, or relationship shifts that appear only in scene synopsis (not supplied here) or other future plot beats.
        """;

    /// <summary>No prior scene: infer entry state by working backward from this scene's brief and optional prose.</summary>
    private const string FirstSceneBackwardInferenceRule =
        """
        BACKWARD INFERENCE (no prior scene handoff): There is no previous scene end-state to carry forward.
        The synopsis/instructions and any scene prose below describe events that occur DURING this scene — after scene entry.
        Infer the narrative state at scene ENTRY: the instant before the first beat of action in the synopsis or the opening line of the prose.
        Work backward — remove injuries, deaths, revelations, location changes, and relationship shifts that the synopsis or prose establish as happening during this scene; reconstruct who is on stage, where they are, baseline mood, clothing, pose, and stable facts at open.
        Do not encode synopsis or prose outcomes as already true at entry. Fill the full JSON snapshot for scene open.
        """;

    private sealed record PipelineProgress(IGenerationProgressNotifier Notifier, Func<long> ElapsedMs);

    private sealed record LlmAuditContext(Guid? GenerationRunId, Guid? BookId)
    {
        public static LlmAuditContext ForRun(Guid runId) => new(runId, null);
        public static LlmAuditContext ForBook(Guid bookId) => new(null, bookId);
    }

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenerationOrchestrator> _logger;
    private readonly IOptions<OllamaOptions> _ollamaOptions;
    private readonly IGenerationRunCancellationRegistry _cancellationRegistry;

    public GenerationOrchestrator(
        IServiceScopeFactory scopeFactory,
        ILogger<GenerationOrchestrator> logger,
        IOptions<OllamaOptions> ollamaOptions,
        IGenerationRunCancellationRegistry cancellationRegistry)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ollamaOptions = ollamaOptions;
        _cancellationRegistry = cancellationRegistry;
    }

    public async Task<Guid> StartGenerationAsync(Guid sceneId, string? idempotencyKey, GenerationStartOptions? options,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();

        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var existing = await db.GenerationRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.SceneId == sceneId && r.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (existing is { Status: GenerationRunStatus.Pending or GenerationRunStatus.Running })
                return existing.Id;
        }

        await CancelActiveRunsForSceneAsync(db, sceneId);

        var run = new GenerationRun
        {
            Id = Guid.NewGuid(),
            SceneId = sceneId,
            Status = GenerationRunStatus.Pending,
            IdempotencyKey = idempotencyKey,
            StartedAt = DateTimeOffset.UtcNow,
            MaxRepairIterations = 5,
            StopAfterDraft = options?.StopAfterDraft ?? false,
            MinWordsOverride = options?.MinWordsOverride,
            MaxWordsOverride = options?.MaxWordsOverride,
            SkipQualityGate = !_ollamaOptions.Value.QualityGateEnabled || (options?.SkipQualityGate == true)
        };
        ApplyQualityThresholdsToRun(run, options, _ollamaOptions.Value);
        db.GenerationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var runId = run.Id;
        var cts = _cancellationRegistry.RegisterRun(runId);
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecutePipelineAsync(runId, cts.Token);
            }
            finally
            {
                _cancellationRegistry.RemoveRun(runId);
            }
        }, CancellationToken.None);

        await notifier.NotifyAsync(runId, "RunStarted", nameof(PipelineStep.PreState),
            "Generation run queued — connecting pipeline…", cancellationToken, 0L, null, null);
        return runId;
    }

    public async Task<bool> CancelGenerationAsync(Guid sceneId, Guid generationRunId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var run = await db.GenerationRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == generationRunId && r.SceneId == sceneId, cancellationToken);
        if (run is null)
            return false;
        if (run.Status is not (GenerationRunStatus.Pending or GenerationRunStatus.Running))
            return false;
        _cancellationRegistry.TryCancel(generationRunId);
        await PersistCancelledRunAsync(generationRunId, pipelineSw: null);
        return true;
    }

    private async Task CancelActiveRunsForSceneAsync(ICreativeLongformDbContext db, Guid sceneId)
    {
        var activeRunIds = await db.GenerationRuns.AsNoTracking()
            .Where(r => r.SceneId == sceneId &&
                        (r.Status == GenerationRunStatus.Pending || r.Status == GenerationRunStatus.Running))
            .Select(r => r.Id)
            .ToListAsync();
        foreach (var activeRunId in activeRunIds)
        {
            _cancellationRegistry.TryCancel(activeRunId);
            await PersistCancelledRunAsync(activeRunId, pipelineSw: null);
        }
    }

    public async Task<FinalizeGenerationResult> FinalizeGenerationAsync(Guid sceneId, Guid generationRunId,
        string? acceptedDraftText, string? approvedStateTableJson, CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var modelPrefs = scope.ServiceProvider.GetRequiredService<IOllamaModelPreferencesService>();
        var writer = await modelPrefs.GetWriterModelAsync(cancellationToken);
        var critic = await modelPrefs.GetCriticModelAsync(cancellationToken);
        var postStateModel = await modelPrefs.GetPostStateModelAsync(cancellationToken);
        var finalizeSw = Stopwatch.StartNew();

        var run = await db.GenerationRuns
            .Include(r => r.Scene)
            .ThenInclude(s => s.Chapter)
            .ThenInclude(c => c.Book)
            .Include(r => r.Scene)
            .ThenInclude(s => s.SceneWorldElements)
            .ThenInclude(swe => swe.WorldElement)
            .FirstOrDefaultAsync(r => r.Id == generationRunId && r.SceneId == sceneId, cancellationToken);
        if (run is null)
            throw new InvalidOperationException("Generation run not found.");
        if (run.Status != GenerationRunStatus.AwaitingUserReview)
            throw new InvalidOperationException("Run is not awaiting review.");

        var finalizeProgress = new PipelineProgress(notifier, () => finalizeSw.ElapsedMilliseconds);

        var scene = run.Scene;
        var book = scene.Chapter.Book;
        var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
        var worldElementIds = scene.SceneWorldElements.Select(swe => swe.WorldElementId).ToHashSet();
        var scopedLinks = await LoadSceneScopedWorldElementLinksAsync(db, worldElementIds, cancellationToken);
        var worldBlock = WorldContextBuilder.Build(book, worldElements, scopedLinks);
        var draftRaw = (acceptedDraftText ?? run.FinalDraftText ?? string.Empty).Trim();
        var draft = ApplyLlmDraftFromModel(scene, draftRaw);
        if (string.IsNullOrEmpty(draft))
            throw new InvalidOperationException("No draft text to finalize.");

        var preSnap = await db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == generationRunId && s.Step == PipelineStep.PreState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var stateBefore = ResolveStateBeforeJsonForRun(preSnap?.StateJson, scene.BeginningStateJson);
        if (LlmJson.IsEmptyJsonObject(stateBefore))
        {
            var previousEnd = await SceneContinuityResolver.GetPreviousSceneEndStateJsonAsync(db, sceneId, cancellationToken);
            var usablePrevious = LlmJson.FirstUsableStateJson(previousEnd);
            if (usablePrevious is not null)
                stateBefore = usablePrevious;
        }
        stateBefore = LlmJson.FirstUsableStateJson(stateBefore) ?? "{}";

        string stateAfter;
        if (!string.IsNullOrWhiteSpace(approvedStateTableJson))
        {
            stateAfter = LlmJson.NormalizeStateJsonOrThrow(approvedStateTableJson.Trim(), "Approved state table JSON");
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }
        else
        {
            var postStateFallback = await ResolvePostStateFallbackAsync(db, generationRunId, scene, stateBefore, cancellationToken);
            await NotifyStepAsync(notifier, generationRunId, PipelineStep.PostState, finalizeProgress.ElapsedMs,
                "Finalize: deriving post-scene state from accepted prose (merged from scene start state).", cancellationToken);
            stateAfter = await ResolvePostStateJsonAsync(
                db, ollama, postStateModel, LlmAuditContext.ForRun(generationRunId), scene,
                stateBefore, draft, worldBlock, postStateFallback, finalizeProgress, cancellationToken,
                failureMessage:
                    "Finalize post-state produced empty JSON and no end-state preview is available. " +
                    "Ensure Ollama is running and Settings → Ollama models has a post-state model configured, " +
                    "set beginning state for this scene, or finalize the previous scene first.");
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }

        if (LlmJson.IsEmptyJsonObject(stateAfter))
            throw new InvalidOperationException(
                "Finalize produced an empty end-state table. Ensure Ollama is running, then try again or edit the state table in the draft workspace before finalizing.");

        await NotifyStepAsync(notifier, generationRunId, PipelineStep.TransitionCheck, finalizeProgress.ElapsedMs,
            "Finalize: continuity check across before / prose / after.", cancellationToken);
        try
        {
            await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, finalizeProgress, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Transition check skipped during finalize (language model unreachable or error).");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Transition check skipped during finalize (invalid LLM response).");
        }

        run.FinalDraftText = draft;
        run.Status = GenerationRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;
        scene.LatestDraftText = draft;
        scene.ManuscriptText = draft;
        scene.ApprovedStateTableJson = stateAfter;
        scene.PendingPostStateJson = null;

        Guid? nextSceneId = null;
        var chapterId = scene.ChapterId;
        var currentOrder = scene.Order;

        var existingNext = await db.Scenes
            .Where(s => s.ChapterId == chapterId && s.Order > currentOrder)
            .OrderBy(s => s.Order)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingNext is not null)
        {
            existingNext.BeginningStateJson = stateAfter;
            nextSceneId = existingNext.Id;
        }
        else if (!scene.Chapter.IsComplete)
        {
            var insertOrder = currentOrder + 1;
            var newId = Guid.NewGuid();
            db.Scenes.Add(new Scene
            {
                Id = newId,
                ChapterId = chapterId,
                Order = insertOrder,
                Title = $"Scene {insertOrder}",
                Synopsis = string.Empty,
                Instructions =
                    "Describe what happens in this scene. Revise this instruction in the scene workflow when you are ready to draft.",
                NarrativePerspective = scene.NarrativePerspective,
                NarrativeTense = scene.NarrativeTense,
                BeginningStateJson = stateAfter
            });
            nextSceneId = newId;
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(generationRunId, "RunFinished", "Succeeded",
            "Finalization complete; approved state saved to the scene.", cancellationToken, finalizeProgress.ElapsedMs(), null, null);
        await DeleteGenerationRunsForSceneAfterFinalizeAsync(db, sceneId, cancellationToken);
        return new FinalizeGenerationResult(stateAfter, nextSceneId);
    }

    /// <summary>
    /// Removes all <see cref="GenerationRun"/> rows for the scene (and cascade-deleted LLM/state/compliance logs).
    /// Called after manuscript finalize so audit data from draft runs does not accumulate indefinitely.
    /// </summary>
    private async Task DeleteGenerationRunsForSceneAfterFinalizeAsync(
        ICreativeLongformDbContext db, Guid sceneId, CancellationToken cancellationToken)
    {
        try
        {
            var runs = await db.GenerationRuns.Where(r => r.SceneId == sceneId).ToListAsync(cancellationToken);
            if (runs.Count == 0)
                return;
            db.GenerationRuns.RemoveRange(runs);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Deleted {Count} generation run(s) for scene {SceneId} after finalize (cascade removes related LLM calls, snapshots, compliance rows).",
                runs.Count, sceneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to delete generation runs for scene {SceneId} after finalize; manuscript is already saved.",
                sceneId);
        }
    }

    public async Task<DeriveBeginningStateResult> DeriveBeginningStateAsync(Guid sceneId,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var modelPrefs = scope.ServiceProvider.GetRequiredService<IOllamaModelPreferencesService>();

        var scene = await db.Scenes
            .Include(s => s.Chapter)
            .ThenInclude(c => c.Book)
            .Include(s => s.SceneWorldElements)
            .ThenInclude(swe => swe.WorldElement)
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            throw new InvalidOperationException("Scene not found.");

        var preStateModel = await modelPrefs.GetPreStateModelAsync(cancellationToken);
        var postStateModel = await modelPrefs.GetPostStateModelAsync(cancellationToken);
        var audit = LlmAuditContext.ForBook(scene.Chapter.BookId);
        var worldBlock = await BuildWorldBlockForSceneAsync(db, scene, cancellationToken);

        var prevSceneId = await SceneContinuityResolver.GetPreviousSceneIdInBookAsync(db, sceneId, cancellationToken);
        string beginningState;
        if (prevSceneId is Guid prevId)
        {
            var prevScene = await db.Scenes.AsNoTracking()
                .Include(s => s.Chapter)
                .ThenInclude(c => c.Book)
                .Include(s => s.SceneWorldElements)
                .ThenInclude(swe => swe.WorldElement)
                .FirstAsync(s => s.Id == prevId, cancellationToken);
            var prevEndApproved = await SceneContinuityResolver.GetSceneEndStateJsonAsync(db, prevId, cancellationToken);
            var prevBeginning = await ResolveSceneBeginningStateJsonAsync(db, prevScene, cancellationToken);
            var prevProse = await ResolveSceneProseForStateDeriveAsync(db, prevScene, cancellationToken);
            if (string.IsNullOrEmpty(prevProse))
                throw new InvalidOperationException(
                    "Previous scene has no manuscript or draft text. Finalize the previous scene in the draft workspace, or generate a draft there first.");

            var prevWorldBlock = await BuildWorldBlockForSceneAsync(db, prevScene, cancellationToken);
            beginningState = await ResolvePostStateJsonAsync(
                db, ollama, postStateModel, audit, prevScene, prevBeginning, prevProse,
                prevWorldBlock, prevEndApproved, progress: null, cancellationToken,
                stepLabel: "Handoff from previous scene",
                failureMessage:
                    "Could not derive beginning state from the previous scene's beginning state and manuscript. " +
                    "Ensure Ollama is running and Settings → Ollama models has a post-state model configured.");
        }
        else
        {
            var currentProse = await ResolveSceneProseForStateDeriveAsync(db, scene, cancellationToken);
            beginningState = await ResolveBeginningStateFromCurrentSceneAsync(
                db, ollama, preStateModel, audit, scene, currentProse, worldBlock, cancellationToken);
        }

        scene.BeginningStateJson = LlmJson.NormalizeStateJsonOrThrow(beginningState, "Beginning state JSON");
        await db.SaveChangesAsync(cancellationToken);

        return new DeriveBeginningStateResult(scene.BeginningStateJson, prevSceneId.HasValue);
    }

    public async Task<ConvertBeginningStateFromProseResult> ConvertBeginningStateFromProseAsync(Guid sceneId,
        string authorProse, CancellationToken cancellationToken = default)
    {
        var prose = authorProse.Trim();
        if (string.IsNullOrEmpty(prose))
            throw new ArgumentException("Beginning state prose is required.", nameof(authorProse));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var modelPrefs = scope.ServiceProvider.GetRequiredService<IOllamaModelPreferencesService>();

        var scene = await db.Scenes
            .Include(s => s.Chapter)
            .ThenInclude(c => c.Book)
            .Include(s => s.SceneWorldElements)
            .ThenInclude(swe => swe.WorldElement)
            .FirstOrDefaultAsync(s => s.Id == sceneId, cancellationToken);
        if (scene is null)
            throw new InvalidOperationException("Scene not found.");

        var preStateModel = await modelPrefs.GetPreStateModelAsync(cancellationToken);
        var audit = LlmAuditContext.ForBook(scene.Chapter.BookId);
        var worldBlock = await BuildWorldBlockForSceneAsync(db, scene, cancellationToken, authorProseBeginningState: true);
        var priorEnd = await SceneContinuityResolver.GetPreviousSceneEndStateJsonAsync(db, sceneId, cancellationToken);

        var beginningState = await ResolveBeginningStateFromProseAsync(
            db, ollama, preStateModel, audit, scene, prose, priorEnd, worldBlock, cancellationToken);

        scene.BeginningStateProse = prose;
        scene.BeginningStateJson = LlmJson.NormalizeStateJsonOrThrow(beginningState, "Beginning state JSON");
        await db.SaveChangesAsync(cancellationToken);

        return new ConvertBeginningStateFromProseResult(scene.BeginningStateJson);
    }

    public async Task<CorrectDraftResult> CorrectDraftAsync(Guid sceneId, Guid generationRunId, string userInstruction,
        string? currentDraftText = null, int? selectionStart = null, int? selectionEnd = null,
        CancellationToken cancellationToken = default)
    {
        var ins = userInstruction.Trim();
        if (string.IsNullOrEmpty(ins))
            throw new ArgumentException("Instruction is required.", nameof(userInstruction));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var modelPrefs = scope.ServiceProvider.GetRequiredService<IOllamaModelPreferencesService>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var writer = await modelPrefs.GetWriterModelAsync(cancellationToken);
        var postStateModel = await modelPrefs.GetPostStateModelAsync(cancellationToken);
        var correctSw = Stopwatch.StartNew();
        var correctProgress = new PipelineProgress(notifier, () => correctSw.ElapsedMilliseconds);

        var run = await db.GenerationRuns
            .Include(r => r.Scene)
            .ThenInclude(s => s.Chapter)
            .ThenInclude(c => c.Book)
            .Include(r => r.Scene)
            .ThenInclude(s => s.SceneWorldElements)
            .ThenInclude(swe => swe.WorldElement)
            .FirstOrDefaultAsync(r => r.Id == generationRunId && r.SceneId == sceneId, cancellationToken);
        if (run is null)
            throw new InvalidOperationException("Generation run not found.");
        if (run.Status != GenerationRunStatus.AwaitingUserReview)
            throw new InvalidOperationException("Run is not awaiting review.");

        var scene = run.Scene;
        var book = scene.Chapter.Book;
        var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
        var worldElementIds = scene.SceneWorldElements.Select(swe => swe.WorldElementId).ToHashSet();
        var scopedLinks = await LoadSceneScopedWorldElementLinksAsync(db, worldElementIds, cancellationToken);
        var worldBlock = WorldContextBuilder.Build(book, worldElements, scopedLinks);
        var draft = !string.IsNullOrWhiteSpace(currentDraftText)
            ? currentDraftText
            : run.FinalDraftText ?? scene.LatestDraftText ?? string.Empty;
        if (string.IsNullOrEmpty(draft))
            throw new InvalidOperationException("No draft to revise.");

        if (selectionStart is null ^ selectionEnd is null)
            throw new ArgumentException("Both selectionStart and selectionEnd must be provided together, or neither.", nameof(selectionStart));

        if (selectionStart is not null && selectionEnd is not null)
        {
            var start = selectionStart.Value;
            var end = selectionEnd.Value;
            if (start < 0 || end > draft.Length || start >= end)
                throw new ArgumentException("Invalid selection range for the draft (use UTF-16 indices; end exclusive, same as a textarea).");
        }

        var preSnap = await db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == generationRunId && s.Step == PipelineStep.PreState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var stateBeforeJson = preSnap?.StateJson ?? "{}";
        var authorizedCastBlock = BuildAuthorizedCastBlock(stateBeforeJson, worldElements, scene);

        var critic = await modelPrefs.GetCriticModelAsync(cancellationToken);
        var qualityCritic = await modelPrefs.GetQualityCriticModelAsync(cancellationToken);
        var correctionModel = EffectiveCorrectionModel(critic);
        var editor = await modelPrefs.GetEditorModelAsync(cancellationToken);
        var agentModel = await modelPrefs.GetAgentModelAsync(cancellationToken);
        var (qualityReviewMin, qualityAcceptMin) = GetQualityScoreThresholds(run);
        var sceneInstructions = SceneInstructionsForAgent(scene);
        var paragraphCount = AgenticEditLoop.SplitParagraphs(draft).Count;
        var (sessionMinWords, sessionMaxWords) = ResolveSessionWordTargets(run);
        var agentTurns = AgentSessionFactory.ComputeMaxTurns(_ollamaOptions.Value, paragraphCount);
        var agentPredict = Math.Max(512, _ollamaOptions.Value.AgenticEditNumPredict);

        var bookContext = await AgentBookContextLoader.LoadAsync(
            db, book, scene, worldElements, scopedLinks, cancellationToken);

        await notifier.NotifyAsync(generationRunId, "StepStarted", nameof(PipelineStep.AgentEdit),
            $"Correct With LLM: agent planning and applying «{ins}» (model «{agentModel}», up to {agentTurns} turns).", cancellationToken,
            correctSw.ElapsedMilliseconds, null, null);

        const int workingDocRevision = 1;
        await WorkingDocumentNotifier.NotifyAsync(
            notifier, generationRunId, workingDocRevision, draft,
            "Correction session opened (current draft)", correctProgress.ElapsedMs, cancellationToken);

        var agentOptions = AgentSessionFactory.Build(new AgentSessionBuildRequest
        {
            Kind = AgentSessionKind.AuthorCorrection,
            OllamaOptions = _ollamaOptions.Value,
            StateBeforeJson = stateBeforeJson,
            AuthorizedCastBlock = authorizedCastBlock,
            BookContext = bookContext,
            BookDirectiveBlock = AgentBookDirectives.Format(book),
            SceneInstructionsBlock = sceneInstructions,
            NarrativePerspective = scene.NarrativePerspective,
            NarrativeTense = scene.NarrativeTense,
            ExpectedEndNotes = scene.ExpectedEndStateNotes,
            ParagraphCount = paragraphCount,
            InitialWorkingDocumentRevision = workingDocRevision,
            SkipQualityGate = false,
            QualityReviewMinScore = qualityReviewMin,
            QualityAcceptMinScore = qualityAcceptMin,
            MinWordsTarget = sessionMinWords,
            MaxWordsTarget = sessionMaxWords,
            UserCorrectionMission = ins,
            SelectionStart = selectionStart,
            SelectionEnd = selectionEnd,
            Delegates = BuildAgentDelegates(db, ollama, writer, critic, qualityCritic, correctionModel, editor, run, scene,
                stateBeforeJson, worldBlock, authorizedCastBlock, correctProgress)
        });

        var text = await AgenticEditLoop.RunAsync(
            draft,
            sceneInstructions,
            scene.ExpectedEndStateNotes,
            worldBlock,
            agentTurns,
            _logger,
            async (system, user, ct) =>
            {
                var o = new OllamaChatOptions { NumPredict = agentPredict };
                return await ChatAndLogForRunAsync(db, ollama, agentModel, run.Id, PipelineStep.AgentEdit, system, user,
                    jsonFormat: true, o, ct, correctProgress, "Correct With LLM agent turn");
            },
            notifier,
            generationRunId,
            correctProgress.ElapsedMs,
            cancellationToken,
            agentOptions);
        text = GuardDraftProse(text, generationRunId, "correct draft agent", stateBeforeJson);
        await notifier.NotifyAsync(generationRunId, "StepCompleted", nameof(PipelineStep.AgentEdit),
            "Correct With LLM agent finished.", cancellationToken, correctSw.ElapsedMilliseconds, null, null);
        text = ApplyLlmDraftFromModel(scene, text);
        run.FinalDraftText = text;
        scene.LatestDraftText = text;
        await db.SaveChangesAsync(cancellationToken);

        var postState = await ResolvePostStateJsonAsync(
            db, ollama, postStateModel, LlmAuditContext.ForRun(generationRunId), scene,
            stateBeforeJson, text, worldBlock,
            LlmJson.FirstUsableStateJson(scene.PendingPostStateJson, stateBeforeJson),
            progress: null, cancellationToken, stepLabel: "Correct draft post-state");
        await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, postState, cancellationToken);
        scene.PendingPostStateJson = postState;
        await db.SaveChangesAsync(cancellationToken);
        return new CorrectDraftResult(text, postState, scene.LlmThinkingNotes);
    }

    private async Task ExecutePipelineAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var modelPrefs = scope.ServiceProvider.GetRequiredService<IOllamaModelPreferencesService>();
        var writer = await modelPrefs.GetWriterModelAsync(cancellationToken);
        var critic = await modelPrefs.GetCriticModelAsync(cancellationToken);
        var qualityCritic = await modelPrefs.GetQualityCriticModelAsync(cancellationToken);
        var correctionModel = EffectiveCorrectionModel(critic);
        var editor = await modelPrefs.GetEditorModelAsync(cancellationToken);
        var agentModel = await modelPrefs.GetAgentModelAsync(cancellationToken);
        var preStateModel = await modelPrefs.GetPreStateModelAsync(cancellationToken);
        var postStateModel = await modelPrefs.GetPostStateModelAsync(cancellationToken);
        var pipelineSw = Stopwatch.StartNew();
        var progress = new PipelineProgress(notifier, () => pipelineSw.ElapsedMilliseconds);

        GenerationRun run;
        try
        {
            run = await db.GenerationRuns
                .Include(r => r.Scene)
                    .ThenInclude(s => s.Chapter)
                        .ThenInclude(c => c.Book)
                .Include(r => r.Scene)
                    .ThenInclude(s => s.SceneWorldElements)
                    .ThenInclude(swe => swe.WorldElement)
                .FirstAsync(r => r.Id == runId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PersistCancelledRunAsync(runId, pipelineSw);
            return;
        }

        run.MaxRepairIterations = Math.Max(1, run.MaxRepairIterations);
        run.Status = GenerationRunStatus.Running;
        run.Scene.PendingPostStateJson = null;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var scene = run.Scene;
            var book = scene.Chapter.Book;
            var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
            var worldElementIds = scene.SceneWorldElements.Select(swe => swe.WorldElementId).ToHashSet();
            var scopedLinks = await LoadSceneScopedWorldElementLinksAsync(db, worldElementIds, cancellationToken);
            var worldBlock = WorldContextBuilder.Build(book, worldElements, scopedLinks);
            var minWords = Math.Max(100, run.MinWordsOverride ?? _ollamaOptions.Value.DraftMinWords);
            var defaultMaxTarget = Math.Min(2000, Math.Max(minWords, 1500));
            var maxTargetWords = run.MaxWordsOverride ?? defaultMaxTarget;
            if (maxTargetWords < minWords)
                maxTargetWords = minWords;

            await NotifyStepAsync(notifier, runId, PipelineStep.PreState, progress.ElapsedMs,
                "Pre-state: resolving beginning narrative state (author JSON, prior scene, or LLM).", cancellationToken);
            var stateBefore = await ResolveBeginningStateAsync(db, ollama, preStateModel, run, scene, worldBlock, runId, progress, cancellationToken);
            await SaveSnapshotAsync(db, runId, PipelineStep.PreState, stateBefore, cancellationToken);
            var authorizedCastBlock = BuildAuthorizedCastBlock(stateBefore, worldElements, scene);

            await NotifyStepAsync(notifier, runId, PipelineStep.Draft, progress.ElapsedMs,
                $"Draft: asking model «{writer}» to produce the scene prose.", cancellationToken);
            var draft = await GenerateDraftAsync(db, ollama, writer, run, scene, stateBefore, worldBlock, minWords, maxTargetWords, progress, cancellationToken);
            var workingDocRevision = 1;
            await WorkingDocumentNotifier.NotifyAsync(
                notifier, runId, workingDocRevision, draft, "Initial draft from writer",
                progress.ElapsedMs, cancellationToken);

            if (_ollamaOptions.Value.AgenticEditEnabled && _ollamaOptions.Value.AgenticEditMaxTurns > 0)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.AgentEdit, progress.ElapsedMs,
                    "Agent edit: orchestrator loop (planning, beat checklist, compliance, writer/editor/corrector delegation, verification).", cancellationToken);
                var sceneInstructions = SceneInstructionsForAgent(scene);
                var paragraphCount = AgenticEditLoop.SplitParagraphs(draft).Count;
                var agentTurns = AgentSessionFactory.ComputeMaxTurns(_ollamaOptions.Value, paragraphCount);
                var agentPredict = Math.Max(512, _ollamaOptions.Value.AgenticEditNumPredict);
                var (pipelineQualityReviewMin, pipelineQualityAcceptMin) = GetQualityScoreThresholds(run);
                var bookContext = await AgentBookContextLoader.LoadAsync(
                    db, book, scene, worldElements, scopedLinks, cancellationToken);
                var agentOptions = AgentSessionFactory.Build(new AgentSessionBuildRequest
                {
                    Kind = AgentSessionKind.PipelinePostDraft,
                    OllamaOptions = _ollamaOptions.Value,
                    StateBeforeJson = stateBefore,
                    AuthorizedCastBlock = authorizedCastBlock,
                    BookContext = bookContext,
                    BookDirectiveBlock = AgentBookDirectives.Format(book),
                    SceneInstructionsBlock = sceneInstructions,
                    NarrativePerspective = scene.NarrativePerspective,
                    NarrativeTense = scene.NarrativeTense,
                    ExpectedEndNotes = scene.ExpectedEndStateNotes,
                    ParagraphCount = paragraphCount,
                    InitialWorkingDocumentRevision = workingDocRevision,
                    SkipQualityGate = run.SkipQualityGate,
                    QualityReviewMinScore = pipelineQualityReviewMin,
                    QualityAcceptMinScore = pipelineQualityAcceptMin,
                    MinWordsTarget = minWords,
                    MaxWordsTarget = maxTargetWords,
                    Delegates = BuildAgentDelegates(db, ollama, writer, critic, qualityCritic, correctionModel, editor, run, scene,
                        stateBefore, worldBlock, authorizedCastBlock, progress)
                });
                draft = await AgenticEditLoop.RunAsync(
                    draft,
                    sceneInstructions,
                    scene.ExpectedEndStateNotes,
                    worldBlock,
                    agentTurns,
                    _logger,
                    async (system, user, ct) =>
                    {
                        var o = new OllamaChatOptions { NumPredict = agentPredict };
                        return await ChatAndLogForRunAsync(db, ollama, agentModel, run.Id, PipelineStep.AgentEdit, system, user,
                            jsonFormat: true, o, ct, progress, "Agent orchestrator turn (JSON tools)");
                    },
                    notifier,
                    runId,
                    progress.ElapsedMs,
                    cancellationToken,
                    agentOptions);
                draft = GuardDraftProse(draft, runId, "agentic edit", stateBefore);
            }

            if (!run.StopAfterDraft)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.PostState, progress.ElapsedMs,
                    "Post-state: deriving narrative state from the finished prose.", cancellationToken);
                var stateAfter = await ResolvePostStateJsonAsync(
                    db, ollama, postStateModel, LlmAuditContext.ForRun(runId), scene,
                    stateBefore, draft, worldBlock, stateBefore, progress, cancellationToken,
                    stepLabel: "Pipeline post-state");
                await SaveSnapshotAsync(db, runId, PipelineStep.PostState, stateAfter, cancellationToken);

                await NotifyStepAsync(notifier, runId, PipelineStep.TransitionCheck, progress.ElapsedMs,
                    "Transition check: verifying continuity before → prose → after.", cancellationToken);
                var transitionOk = await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, progress, cancellationToken);
                if (!transitionOk)
                    _logger.LogWarning("Transition check reported gaps for run {RunId}", runId);
            }

            var text = ApplyLlmDraftFromModel(scene, draft);

            var (reviewMin, acceptMin) = GetQualityScoreThresholds(run);

            await NotifyStepAsync(notifier, runId, PipelineStep.Compliance, progress.ElapsedMs,
                "Compliance: checking scene instructions, narrative voice, grammar/punctuation, and world context.", cancellationToken);
            var lastCompliance = await EvaluateComplianceAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, authorizedCastBlock, progress, cancellationToken);
            if (!lastCompliance.Pass)
            {
                await notifier.NotifyAsync(runId, "DraftReviewNote", PipelineStep.Compliance.ToString(),
                    BuildComplianceIssuesOnlyDetail(lastCompliance),
                    cancellationToken, progress.ElapsedMs(), null, null);
            }

            QualityVerdict? lastQuality = null;
            if (!run.SkipQualityGate)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.Quality, progress.ElapsedMs,
                    $"Quality: numeric prose score (pass ≥{reviewMin:0.#}; polish target ≥{acceptMin:0.#}).", cancellationToken);
                lastQuality = await EvaluateQualityAsync(db, ollama, qualityCritic, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
                var q = lastQuality.Score ?? 0;
                if (q < reviewMin)
                {
                    await notifier.NotifyAsync(runId, "DraftReviewNote", PipelineStep.Quality.ToString(),
                        BuildQualityScoreNoteDetail(lastQuality, q, reviewMin, acceptMin),
                        cancellationToken, progress.ElapsedMs(), null, null);
                }
            }
            else
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.Quality, progress.ElapsedMs,
                    "Quality gate skipped (configuration or request).", cancellationToken);
            }

            if (_ollamaOptions.Value.AgenticRepassEnabled
                && _ollamaOptions.Value.AgenticEditEnabled
                && _ollamaOptions.Value.AgenticEditMaxTurns > 0
                && (!lastCompliance.Pass || (lastQuality is not null && (lastQuality.Score ?? 0) < reviewMin)))
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.AgentEdit, progress.ElapsedMs,
                    "Agent remediation: terminal compliance or quality failed — running a focused second agent pass.", cancellationToken);
                var remediationMission = BuildRemediationMission(lastCompliance, lastQuality, reviewMin);
                var repassParagraphCount = AgenticEditLoop.SplitParagraphs(text).Count;
                var repassTurns = AgentSessionFactory.ComputeMaxTurns(_ollamaOptions.Value, repassParagraphCount);
                var repassOptions = AgentSessionFactory.Build(new AgentSessionBuildRequest
                {
                    Kind = AgentSessionKind.AuthorCorrection,
                    OllamaOptions = _ollamaOptions.Value,
                    StateBeforeJson = stateBefore,
                    AuthorizedCastBlock = authorizedCastBlock,
                    BookContext = await AgentBookContextLoader.LoadAsync(db, book, scene, worldElements, scopedLinks, cancellationToken),
                    BookDirectiveBlock = AgentBookDirectives.Format(book),
                    SceneInstructionsBlock = SceneInstructionsForAgent(scene),
                    NarrativePerspective = scene.NarrativePerspective,
                    NarrativeTense = scene.NarrativeTense,
                    ExpectedEndNotes = scene.ExpectedEndStateNotes,
                    ParagraphCount = repassParagraphCount,
                    InitialWorkingDocumentRevision = workingDocRevision + 1,
                    SkipQualityGate = run.SkipQualityGate,
                    QualityReviewMinScore = reviewMin,
                    QualityAcceptMinScore = acceptMin,
                    MinWordsTarget = minWords,
                    MaxWordsTarget = maxTargetWords,
                    UserCorrectionMission = remediationMission,
                    Delegates = BuildAgentDelegates(db, ollama, writer, critic, qualityCritic, correctionModel, editor, run, scene,
                        stateBefore, worldBlock, authorizedCastBlock, progress)
                });
                text = await AgenticEditLoop.RunAsync(
                    text,
                    SceneInstructionsForAgent(scene),
                    scene.ExpectedEndStateNotes,
                    worldBlock,
                    repassTurns,
                    _logger,
                    async (system, user, ct) =>
                    {
                        var o = new OllamaChatOptions { NumPredict = Math.Max(512, _ollamaOptions.Value.AgenticEditNumPredict) };
                        return await ChatAndLogForRunAsync(db, ollama, agentModel, run.Id, PipelineStep.AgentEdit, system, user,
                            jsonFormat: true, o, ct, progress, "Agent remediation pass (JSON tools)");
                    },
                    notifier,
                    runId,
                    progress.ElapsedMs,
                    cancellationToken,
                    repassOptions);
                text = GuardDraftProse(text, runId, "agent remediation", stateBefore);
                text = ApplyLlmDraftFromModel(scene, text);

                lastCompliance = await EvaluateComplianceAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, authorizedCastBlock, progress, cancellationToken);
                if (!run.SkipQualityGate)
                    lastQuality = await EvaluateQualityAsync(db, ollama, qualityCritic, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
            }

            run.FinalDraftText = text;
            scene.LatestDraftText = text;
            await db.SaveChangesAsync(cancellationToken);

            if (run.StopAfterDraft)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsRunCancelledAsync(db, runId, cancellationToken))
                    return;

                await NotifyStepAsync(notifier, runId, PipelineStep.PostState, progress.ElapsedMs,
                    "Post-state: deriving end-of-scene narrative table from the draft (for review).", cancellationToken);
                var postStateForReview = await ResolvePostStateJsonAsync(
                    db, ollama, postStateModel, LlmAuditContext.ForRun(runId), scene,
                    stateBefore, text, worldBlock, stateBefore, progress, cancellationToken,
                    stepLabel: "Draft review post-state");
                await SaveSnapshotAsync(db, runId, PipelineStep.PostState, postStateForReview, cancellationToken);
                scene.PendingPostStateJson = postStateForReview;
                await db.SaveChangesAsync(cancellationToken);

                run.Status = GenerationRunStatus.AwaitingUserReview;
                run.CompletedAt = null;
                await db.SaveChangesAsync(cancellationToken);
                await notifier.NotifyAsync(runId, "RunFinished", "AwaitingUserReview",
                    "Draft is ready for your review in the app.", cancellationToken, progress.ElapsedMs(), null, null);
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsRunCancelledAsync(db, runId, cancellationToken))
                    return;

                run.Status = GenerationRunStatus.Succeeded;
                run.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await notifier.NotifyAsync(runId, "RunFinished", "Succeeded",
                    "Pipeline completed; scene and state snapshots saved.", cancellationToken, progress.ElapsedMs(), null, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await PersistCancelledRunAsync(runId, pipelineSw);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed for run {RunId}", runId);
            run = await db.GenerationRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
            run.Status = GenerationRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            await notifier.NotifyAsync(runId, "RunFinished", "Failed", ex.Message, CancellationToken.None,
                pipelineSw.ElapsedMilliseconds, null, null);
        }
    }

    private static string ApplyLlmDraftFromModel(Scene scene, string rawText)
    {
        var split = LlmProseSanitizer.SplitThinkingFromProse(rawText);
        if (!string.IsNullOrWhiteSpace(split.ThinkingNotes))
        {
            scene.LlmThinkingNotes = string.IsNullOrWhiteSpace(scene.LlmThinkingNotes)
                ? split.ThinkingNotes
                : $"{scene.LlmThinkingNotes.Trim()}\n\n---\n\n{split.ThinkingNotes.Trim()}";
        }
        return DraftProseGuard.TrimOpeningStateRecitation(
            DraftProseGuard.TrimRepetitiveLoops(split.Prose),
            scene.BeginningStateJson);
    }

    private static async Task<bool> IsRunCancelledAsync(
        ICreativeLongformDbContext db,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var status = await db.GenerationRuns.AsNoTracking()
            .Where(r => r.Id == runId)
            .Select(r => r.Status)
            .FirstAsync(cancellationToken);
        return status == GenerationRunStatus.Cancelled;
    }

    private async Task PersistCancelledRunAsync(Guid runId, Stopwatch? pipelineSw)
    {
        _logger.LogInformation("Generation cancelled for run {RunId}", runId);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var run = await db.GenerationRuns.FirstAsync(r => r.Id == runId, CancellationToken.None);
        if (run.Status is not (GenerationRunStatus.Pending or GenerationRunStatus.Running))
            return;
        run.Status = GenerationRunStatus.Cancelled;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.FailureReason = "Cancelled by user.";
        await db.SaveChangesAsync(CancellationToken.None);
        await notifier.NotifyAsync(runId, "RunFinished", "Cancelled",
            "Generation was cancelled.", CancellationToken.None, pipelineSw?.ElapsedMilliseconds, null, null);
    }

    /// <summary>Links whose both endpoints are in the scene-attached element set (for prompt inclusion).</summary>
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

    private static async Task NotifyStepAsync(
        IGenerationProgressNotifier notifier,
        Guid runId,
        PipelineStep step,
        Func<long> elapsedMs,
        string detail,
        CancellationToken cancellationToken)
    {
        await notifier.NotifyAsync(runId, "StepStarted", step.ToString(), detail, cancellationToken,
            elapsedMs(), null, null);
    }

    private static async Task<string?> GetLatestSucceededPostStateJsonAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        Guid currentRunId,
        CancellationToken cancellationToken)
    {
        var prevRun = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == sceneId && r.Id != currentRunId && r.Status == GenerationRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (prevRun is null)
            return null;

        var snap = await db.StateSnapshots
            .AsNoTracking()
            .Where(s => s.GenerationRunId == prevRun.Id && s.Step == PipelineStep.PostState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return snap?.StateJson;
    }

    private static async Task<string?> GetLastSucceededPostStateJsonForSceneAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken)
        => await SceneContinuityResolver.GetSceneEndStateJsonAsync(db, sceneId, cancellationToken);

    private static async Task<string?> GetPreviousSceneLastPostStateJsonAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken)
        => await SceneContinuityResolver.GetPreviousSceneEndStateJsonAsync(db, sceneId, cancellationToken);

    private static async Task<Guid?> GetPreviousSceneIdAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken)
        => await SceneContinuityResolver.GetPreviousSceneIdInBookAsync(db, sceneId, cancellationToken);

    private static async Task<string> ResolveSceneBeginningStateJsonAsync(
        ICreativeLongformDbContext db,
        Scene scene,
        CancellationToken cancellationToken)
    {
        var fromRun = await GetLatestPreStateSnapshotJsonForSceneAsync(db, scene.Id, cancellationToken);
        return LlmJson.FirstUsableStateJson(scene.BeginningStateJson, fromRun) ?? "{}";
    }

    private static async Task<string?> GetLatestPreStateSnapshotJsonForSceneAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var run = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == sceneId && r.Status == GenerationRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (run is null)
            return null;
        var snap = await db.StateSnapshots
            .AsNoTracking()
            .Where(s => s.GenerationRunId == run.Id && s.Step == PipelineStep.PreState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return snap?.StateJson;
    }

    private static async Task<string?> ResolveSceneProseForStateDeriveAsync(
        ICreativeLongformDbContext db,
        Scene scene,
        CancellationToken cancellationToken)
    {
        var manuscript = scene.ManuscriptText?.Trim();
        if (!string.IsNullOrEmpty(manuscript))
            return manuscript;

        var succeededRun = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == scene.Id && r.Status == GenerationRunStatus.Succeeded)
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var succeededDraft = succeededRun?.FinalDraftText?.Trim();
        if (!string.IsNullOrEmpty(succeededDraft))
            return succeededDraft;

        var reviewRun = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == scene.Id && r.Status == GenerationRunStatus.AwaitingUserReview)
            .OrderByDescending(r => r.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var reviewDraft = reviewRun?.FinalDraftText?.Trim();
        if (!string.IsNullOrEmpty(reviewDraft))
            return reviewDraft;

        var latest = scene.LatestDraftText?.Trim();
        return string.IsNullOrEmpty(latest) ? null : latest;
    }

    private static async Task<string> BuildWorldBlockForSceneAsync(
        ICreativeLongformDbContext db,
        Scene scene,
        CancellationToken cancellationToken,
        bool authorProseBeginningState = false)
    {
        var book = scene.Chapter.Book;
        var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
        var worldElementIds = scene.SceneWorldElements.Select(swe => swe.WorldElementId).ToHashSet();
        var scopedLinks = await LoadSceneScopedWorldElementLinksAsync(db, worldElementIds, cancellationToken);
        return WorldContextBuilder.Build(book, worldElements, scopedLinks, authorProseBeginningState);
    }

    private async Task<string> ResolveBeginningStateAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string preStateModel,
        GenerationRun run,
        Scene scene,
        string worldBlock,
        Guid runId,
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var notifier = progress.Notifier;
        if (!string.IsNullOrWhiteSpace(scene.BeginningStateJson))
        {
            await notifier.NotifyAsync(runId, "StepStarted", "BeginningState",
                "Using author-provided beginning state JSON (no extra LLM call).", cancellationToken,
                progress.ElapsedMs(), null, null);
            return scene.BeginningStateJson.Trim();
        }

        var fromPrev = await GetPreviousSceneLastPostStateJsonAsync(db, scene.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fromPrev))
        {
            await notifier.NotifyAsync(runId, "StepStarted", "BeginningState",
                "Seeding state from the previous scene’s approved end-state table.", cancellationToken,
                progress.ElapsedMs(), null, null);
            return fromPrev.Trim();
        }

        var sameScenePrior = await GetLatestSucceededPostStateJsonAsync(db, scene.Id, runId, cancellationToken);
        await notifier.NotifyAsync(runId, "StepStarted", "BeginningState",
            "No author or prior-scene state — asking the pre-state model to infer pre-scene JSON.", cancellationToken,
            progress.ElapsedMs(), null, null);
        return await GeneratePreStateAsync(db, ollama, preStateModel, LlmAuditContext.ForRun(runId), scene, sameScenePrior, worldBlock, progress, cancellationToken);
    }

    /// <summary>Prefer the run’s pre-state snapshot; if missing or empty, use author beginning-state JSON on the scene.</summary>
    private static string ResolveStateBeforeJsonForRun(string? preStateSnapshotJson, string? sceneBeginningStateJson)
    {
        return LlmJson.FirstUsableStateJson(preStateSnapshotJson, sceneBeginningStateJson) ?? "{}";
    }

    private static string SceneInstructionsForAgent(Scene scene)
    {
        var syn = scene.Synopsis?.Trim();
        var ins = scene.Instructions?.Trim() ?? "";
        if (string.IsNullOrEmpty(syn))
            return ins;
        if (string.IsNullOrEmpty(ins))
            return syn;
        return $"{syn}\n\nAdditional instructions: {ins}";
    }

    private static string AgentDelegationWaitLabel(string role, AgentWriterInvokeRequest req)
    {
        var target = !string.IsNullOrWhiteSpace(req.FocusExcerpt) ? req.FocusExcerpt : req.SpanText;
        return $"{role} is reworking {AgentEditNarrative.OptionalQuote(target, $"paragraphs {req.ParagraphStart}..{req.ParagraphEnd}")}";
    }

    private async Task<string> GeneratePreStateAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string? priorStateJson,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You output ONLY valid JSON matching the narrative state snapshot. No markdown fences.
            """
            + NarrativeStateJsonSchemaPrompt
            + PreSceneSynopsisBoundaryRule
            + """
            PRE-SCENE snapshot: continuity at scene entry only — not after any synopsis beat.
            - If prior state JSON is non-empty, carry forward what still holds at entry; do not import synopsis outcomes into pre-state (see temporal boundary above).
            - Adjust only starting situation: who is on stage, where they are, baseline mood before the inciting action, stable facts true before the first line of prose.
            - Fill concrete values: environment.setting, spatial layout/proximity, each on-stage character’s pose, clothing, emotionalState, relativeToOthers, topOfMind as true at entry — not after fights, injuries, or reveals described in the synopsis.
            - traitsShownNotTold: short cues for showing traits through action, not abstract labels (show, don't tell).
            """
            + ShowDontTellEmphasis
            + InventionScopeHardRule
            + """
            JSON must not invent named characters, relationships, or plot facts absent from the synopsis/instructions, linked world-building, prior state, or (when inferring) defensible texture that does not imply new named entities or events.
            """;
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from story tone if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from story tone if not specified)"}
            Prior state JSON (may be empty — previous scene end-state or author seed): {priorStateJson ?? "{}"}

            {worldContextBlock}

            Produce pre-scene state only: before anything in the synopsis happens. Do not reflect events, injuries, or outcomes that the synopsis describes as occurring in this scene.
            """;
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PreState, system, user, jsonFormat: true, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress,
            "Infer beginning narrative state (JSON)");
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> GeneratePreStateFromCurrentSceneAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string? sceneProse,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You output ONLY valid JSON matching the narrative state snapshot. No markdown fences.
            """
            + NarrativeStateJsonSchemaPrompt
            + FirstSceneBackwardInferenceRule
            + PreSceneSynopsisBoundaryRule
            + """
            Fill concrete values at scene entry: environment, spatial layout, each on-stage character's pose, clothing, emotionalState, relativeToOthers, topOfMind, traitsShownNotTold.
            """
            + ShowDontTellEmphasis
            + InventionScopeHardRule;
        var proseBlock = string.IsNullOrWhiteSpace(sceneProse)
            ? "Scene prose: (none yet — infer entry state from synopsis/instructions and linked world-building only)."
            : $"""
              Scene prose (draft or manuscript for THIS scene — read what changes during the text, then rewind to the opening instant before those changes):
              {sceneProse.Trim()}
              """;
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions (events in this scene occur AFTER entry — use only to infer what state was before they happened):
            {SceneInstructionsForAgent(scene)}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from story tone if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from story tone if not specified)"}
            Prior scene end-state: (none — first scene or no prior handoff)

            {proseBlock}

            {worldContextBlock}

            Produce pre-scene state JSON only: the instant before anything in the synopsis or prose happens.
            """;
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PreState, system, user,
            jsonFormat: true, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress,
            "Infer beginning state by working backward from scene brief");
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> GeneratePreStateFromCurrentSceneSimplifiedAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string? sceneProse,
        string worldContextBlock,
        bool jsonFormat,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            Infer scene-opening narrative state JSON by working backward from the synopsis and optional prose below.
            Output ONLY one JSON object with schemaVersion 1 and filled characters, spatial, environment fields.
            Entry state is BEFORE any event described in the synopsis or prose. Never return {}.
            """
            + InventionScopeHardRule;
        const string exampleShape =
            """{"schemaVersion":1,"transitionSummary":"…","characters":[{"name":"…","location":"…","pose":"…","clothing":"…","emotionalState":"…","relativeToOthers":"…","topOfMind":["…"],"traitsShownNotTold":["…"]}],"spatial":{"layout":"…","proximity":"…"},"dialogue":{"topic":null,"unresolved":[]},"knowledge":{"povBeliefs":[],"omniscientFacts":[]},"environment":{"setting":"…","timeOfDay":"…","weather":"…","sensory":[]},"plotDevices":[]}""";
        var proseLine = string.IsNullOrWhiteSpace(sceneProse)
            ? "(no prose yet)"
            : sceneProse.Trim();
        var user = $"""
            Scene: {scene.Title}
            Synopsis/instructions: {SceneInstructionsForAgent(scene)}
            Scene prose (optional — rewind to before changes in this text): {proseLine}

            {worldContextBlock}

            Example shape (fill with concrete entry-state values inferred backward from the brief):
            {exampleShape}
            """;
        var progressLabel = jsonFormat
            ? "Infer beginning state backward (simplified JSON mode)"
            : "Infer beginning state backward (simplified prose JSON)";
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PreState, system, user,
            jsonFormat, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress, progressLabel);
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> ResolveBeginningStateFromCurrentSceneAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string? sceneProse,
        string worldContextBlock,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = (await GeneratePreStateFromCurrentSceneAsync(db, ollama, model, audit, scene, sceneProse,
                worldContextBlock, progress: null, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("Beginning state from current scene brief returned empty or invalid JSON; retrying.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var useJsonFormat = attempt == 0;
            var json = (await GeneratePreStateFromCurrentSceneSimplifiedAsync(db, ollama, model, audit, scene,
                sceneProse, worldContextBlock, useJsonFormat, progress: null, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("Beginning state backward simplified pass returned empty or invalid JSON; retrying without JSON mode.");
        }

        throw new InvalidOperationException(
            "Could not infer beginning state from this scene's synopsis. " +
            "Ensure Ollama is running and Settings → Ollama models has a pre-state model configured, " +
            "or enter beginning state on the Prose or JSON tab.");
    }

    private async Task<string> ResolveBeginningStateFromProseAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string authorProse,
        string? priorStateJson,
        string worldContextBlock,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = (await GeneratePreStateFromAuthorProseAsync(db, ollama, model, audit, scene, authorProse,
                priorStateJson, worldContextBlock, progress: null, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("Beginning state from prose returned empty JSON; retrying full conversion.");
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var useJsonFormat = attempt == 0;
            var json = (await GeneratePreStateFromAuthorProseSimplifiedAsync(db, ollama, model, audit, scene,
                authorProse, priorStateJson, worldContextBlock, useJsonFormat, progress: null, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("Beginning state from prose simplified pass returned empty JSON; retrying without JSON mode.");
        }

        var fallback = LlmJson.FirstUsableStateJson(priorStateJson, scene.BeginningStateJson);
        if (fallback is not null)
        {
            _logger.LogWarning(
                "Beginning state from prose could not be converted; using continuity JSON fallback for scene {SceneId}.",
                scene.Id);
            return fallback;
        }

        throw new InvalidOperationException(
            "Could not convert beginning-state prose to JSON — the pre-state model returned an empty object after several attempts. " +
            "Confirm Ollama is running and Settings → Ollama models has a pre-state model set (or uses the writer model). " +
            "You can edit the JSON tab directly, or use Regenerate with LLM on the JSON tab.");
    }

    private async Task<string> GeneratePreStateFromAuthorProseAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string authorProse,
        string? priorStateJson,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You convert the author's plain-language description of scene-opening narrative state into the canonical JSON snapshot schema. Output ONLY valid JSON. No markdown fences.
            """
            + NarrativeStateJsonSchemaPrompt
            + AuthorProsePreStateBoundaryRule
            + """
            Fill concrete values in every applicable field from the author prose. Never return an empty JSON object.
            """
            + ShowDontTellEmphasis
            + InventionScopeFromAuthorProseRule;
        var user = $"""
            Author beginning-state description (plain language — translate this into the JSON snapshot):
            {authorProse}

            Scene title (label only): {scene.Title}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from author prose if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from author prose if not specified)"}
            Prior scene end-state JSON (continuity context only; may be empty): {priorStateJson ?? "{}"}

            {worldContextBlock}

            Produce pre-scene state JSON from the author prose only. Do not infer events from scene synopsis or instructions — they are intentionally omitted.
            """;
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PreState, system, user,
            jsonFormat: true, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress,
            "Convert author prose to beginning narrative state (JSON)");
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> GeneratePreStateFromAuthorProseSimplifiedAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string authorProse,
        string? priorStateJson,
        string worldContextBlock,
        bool jsonFormat,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            Convert the author's plain-language scene-opening description into narrative state JSON.
            Output ONLY one JSON object matching schemaVersion 1 with characters, spatial, environment, dialogue, knowledge, and plotDevices arrays/objects filled from the prose.
            Include every character named in the prose. Never return an empty object {}.
            Scene synopsis and instructions are not provided — do not infer future scene beats.
            """
            + InventionScopeFromAuthorProseRule;
        var priorJson = string.IsNullOrWhiteSpace(priorStateJson) ? "{}" : priorStateJson.Trim();
        const string exampleShape =
            """{"schemaVersion":1,"transitionSummary":"…","characters":[{"name":"…","location":"…","pose":"…","clothing":"…","emotionalState":"…","relativeToOthers":"…","topOfMind":["…"],"traitsShownNotTold":["…"]}],"spatial":{"layout":"…","proximity":"…"},"dialogue":{"topic":null,"unresolved":[]},"knowledge":{"povBeliefs":[],"omniscientFacts":[]},"environment":{"setting":"…","timeOfDay":"…","weather":"…","sensory":[]},"plotDevices":[]}""";
        var user = $"""
            Author prose (sole narrative source — translate into JSON fields):
            {authorProse}

            Scene title (label only): {scene.Title}
            Prior scene end-state (continuity context only; may be empty): {priorJson}

            {worldContextBlock}

            Example shape (fill with concrete values from the author prose):
            {exampleShape}
            """;
        var progressLabel = jsonFormat
            ? "Convert author prose (simplified JSON mode)"
            : "Convert author prose (simplified prose JSON)";
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PreState, system, user,
            jsonFormat, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress, progressLabel);
        return LlmJson.StripMarkdownFences(text);
    }

    private OllamaChatOptions CreateJsonStateOptions() =>
        new()
        {
            NumPredict = Math.Max(4096, _ollamaOptions.Value.DraftNumPredict),
            RepeatPenalty = 1.05f,
            RepeatLastN = 128,
            Temperature = 0.15f
        };

    private async Task<string> GenerateDraftAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string stateBeforeJson,
        string worldContextBlock,
        int minWords,
        int maxTargetWords,
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var numPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict);
        var proseOptions = CreateDraftProseOptions(numPredict);

        var system =
            """
            You are a fiction writer producing long-form prose for novels and serial fiction.
            """
            + InventionScopeHardRule
            + ShowDontTellEmphasis
            + BeginningStateContinuityForProseRule
            + """
            Follow the scene synopsis and instructions; use the continuity anchor only for consistency (see above).
            Honor the requested narrative perspective and tense exactly.
            Write vivid prose; avoid naming character traits explicitly when a bio already labels them—show through action and detail.
            Respect story tone and linked world-building; do not invent facts that contradict them.
            Cast and world scope: only include or reference characters and world elements (people, places, factions, objects, lore)
            that appear under "Linked world-building" in the user message, or are explicitly named in the scene synopsis/instructions,
            or appear in the continuity anchor. Do not name or reference characters or world elements from the book synopsis,
            book-level notes, or the wider story unless they are covered by those sources—avoid importing the broader cast or canon.
            Develop the scene with multiple paragraphs: setting, action, dialogue, and character interiority as fits the brief.
            Do not stop after a few sentences; this is a full scene beat, not a summary.
            Never repeat the same sentence, paragraph, or story beat. If the synopsis is covered, end the scene — do not loop or pad.
            Output prose only, no preamble or title line.
            """;
        var continuityBrief = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(stateBeforeJson);
        var user = $"""
            {continuityBrief}

            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective: {scene.NarrativePerspective ?? "(infer from story)"}
            Narrative tense: {scene.NarrativeTense ?? "(infer from story)"}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

            {worldContextBlock}

            Write the complete scene. Target roughly {minWords}–{maxTargetWords} words for this session unless the brief explicitly demands a shorter piece.
            """;
        var (text, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.Draft, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Write scene draft (prose)");
        text = GuardDraftProse(text, run.Id, "initial draft", stateBeforeJson);

        if (_ollamaOptions.Value.DraftExpandIfShort && CountWords(text) < minWords)
        {
            _logger.LogInformation(
                "Draft short ({Words} words, min {Min}); running expansion pass for run {RunId}",
                CountWords(text), minWords, run.Id);
            await NotifyStepAsync(progress.Notifier, run.Id, PipelineStep.Draft, progress.ElapsedMs,
                $"Draft expansion: first pass was {CountWords(text)} words (target ≥{minWords}); asking model «{model}» to continue the scene.",
                cancellationToken);
            var expandSystem =
                """
                You expand fiction for long-form publication. Continue in the same voice, tense, and POV.
                """
                + InventionScopeHardRule
                + ShowDontTellEmphasis
                + BeginningStateContinuityForProseRule
                + """
                Output ONLY the NEW paragraphs that should follow the draft in the user message.
                Do NOT repeat, paraphrase, or recap any sentence from the existing draft. Do not include the draft text in your output.
                Add substantive prose—new paragraphs, beats, dialogue, sensory detail—not repetition of the same lines.
                Do not summarize the scene; extend it. Output prose only, no preamble.
                Do not introduce new characters, relationships, or plot events that are not already in the draft or grounded in Linked world-building
                and the scene synopsis/instructions in the user message.
                Never loop or repeat the same beat; move the scene forward.
                """;
            var expandUser = $"""
                The draft below is too short for this novel scene. It must reach at least {minWords} words total after your new paragraphs are appended.
                Scene synopsis and instructions:
                {SceneInstructionsForAgent(scene)}
                Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

                {worldContextBlock}

                Existing draft (do not repeat any of this in your output — write only what comes next):
                {text}
                """;
            var (continuation, _, _) = await ChatAndLogForRunAsync(
                db, ollama, model, run.Id, PipelineStep.Draft, expandSystem, expandUser, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
                "Expand short draft to target length");
            text = GuardDraftProse(DraftProseGuard.MergeDraftContinuation(text, continuation), run.Id, "expansion pass", stateBeforeJson);
        }

        return text;
    }

    private OllamaChatOptions CreateDraftProseOptions(int numPredict) =>
        new()
        {
            NumPredict = numPredict,
            RepeatPenalty = _ollamaOptions.Value.DraftRepeatPenalty,
            RepeatLastN = _ollamaOptions.Value.DraftRepeatLastN
        };

    private string GuardDraftProse(string text, Guid runId, string stage, string? stateBeforeJson = null)
    {
        var guarded = DraftProseGuard.TrimRepetitiveLoops(text);
        if (!string.IsNullOrWhiteSpace(stateBeforeJson))
            guarded = DraftProseGuard.TrimOpeningStateRecitation(guarded, stateBeforeJson);
        if (guarded.Length + 50 < text.Length)
        {
            _logger.LogWarning(
                "Trimmed {RemovedChars} characters of repetitive prose from {Stage} for run {RunId}",
                text.Length - guarded.Length, stage, runId);
        }

        return guarded;
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private async Task<string> GeneratePostStateAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string stateBeforeJson,
        string draftText,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You output ONLY valid JSON matching the narrative state snapshot. No markdown fences.
            """
            + NarrativeStateJsonSchemaPrompt
            + PostStateContinuityDeltaSchemaAndRules
            + PostSceneStateMirrorOfPreStateStyle
            + InventionScopeHardRule
            + ShowDontTellEmphasis
            + """
            Grounding: scene prose + state at entry + linked world-building below. Do not invent named characters, relationships, or plot facts absent from those sources (same bar as beginning-state inference).
            """;
        var entryStateBlock = LlmJson.IsEmptyJsonObject(stateBeforeJson)
            ? """
              State at scene ENTRY: (empty — no prior snapshot). Build the end snapshot entirely from scene prose and linked world-building below.
              """
            : $"""
              State at scene ENTRY (JSON — same shape as beginning-state; baseline to merge forward from):
              {stateBeforeJson}
              """;
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from prose if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from prose if not specified)"}

            {entryStateBlock}

            Scene prose (this scene only — read facts from this text into the end state; this block is not JSON):
            {draftText}

            {worldContextBlock}

            Produce post-scene state only: infer the **end** snapshot in the **same format, field completeness, and concrete style** as you would for beginning-state at scene open — but every field must reflect the **last moment after** the prose above. Output JSON only. Never return an empty JSON object.
            """;
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PostState, system, user, jsonFormat: true, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress,
            "Derive post-scene narrative state (JSON, merged from pre-state)");
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> GeneratePostStateSimplifiedAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string stateBeforeJson,
        string draftText,
        string worldContextBlock,
        bool jsonFormat,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            Derive end-of-scene narrative state JSON from the scene prose below.
            Output ONLY one JSON object with schemaVersion 1 and filled characters, spatial, environment, dialogue, knowledge, and plotDevices fields.
            Reflect facts at the LAST moment after the prose. Never return an empty object {}.
            """
            + InventionScopeHardRule;
        var entryJson = LlmJson.FirstUsableStateJson(stateBeforeJson) ?? "{}";
        const string exampleShape =
            """{"schemaVersion":1,"transitionSummary":"…","characters":[{"name":"…","location":"…","pose":"…","clothing":"…","emotionalState":"…","relativeToOthers":"…","topOfMind":["…"],"traitsShownNotTold":["…"]}],"spatial":{"layout":"…","proximity":"…"},"dialogue":{"topic":null,"unresolved":[]},"knowledge":{"povBeliefs":[],"omniscientFacts":[]},"environment":{"setting":"…","timeOfDay":"…","weather":"…","sensory":[]},"plotDevices":[]}""";
        var user = $"""
            Scene title: {scene.Title}
            State at scene entry (baseline to update): {entryJson}

            Scene prose (read end-state facts from this text):
            {draftText}

            {worldContextBlock}

            Example shape (fill with concrete values from the prose):
            {exampleShape}
            """;
        var progressLabel = jsonFormat
            ? "Derive post-scene state (simplified JSON mode)"
            : "Derive post-scene state (simplified prose JSON)";
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, audit, PipelineStep.PostState, system, user,
            jsonFormat, CreateJsonStateOptions(), cancellationToken: cancellationToken, progress, progressLabel);
        return LlmJson.StripMarkdownFences(text);
    }

    private async Task<string> ResolvePostStateJsonAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        Scene scene,
        string stateBeforeJson,
        string draftText,
        string worldContextBlock,
        string? fallbackJson,
        PipelineProgress? progress,
        CancellationToken cancellationToken,
        string stepLabel = "Post-state",
        string? failureMessage = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = (await GeneratePostStateAsync(db, ollama, model, audit, scene, stateBeforeJson, draftText,
                worldContextBlock, progress, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("{Step} returned empty or invalid JSON; retrying full pass.", stepLabel);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var useJsonFormat = attempt == 0;
            var json = (await GeneratePostStateSimplifiedAsync(db, ollama, model, audit, scene, stateBeforeJson,
                draftText, worldContextBlock, useJsonFormat, progress, cancellationToken)).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("{Step} simplified pass returned empty or invalid JSON; retrying without JSON mode.", stepLabel);
        }

        if (LlmJson.TryNormalizeStateJson(fallbackJson, out var fallbackNormalized))
        {
            _logger.LogWarning("{Step} could not be derived from the model; using stored preview or beginning-state fallback.", stepLabel);
            return fallbackNormalized;
        }

        throw new InvalidOperationException(
            failureMessage ??
            $"{stepLabel} produced empty JSON and no usable preview or beginning-state fallback is available. " +
            "Ensure Ollama is running and Settings → Ollama models has a post-state model configured.");
    }

    /// <summary>
    /// Invokes <paramref name="callOnce"/> up to two times. Returns null when both responses are empty JSON objects <c>{}</c>
    /// (caller should apply a safe default and continue — do not feed empty output to repair loops).
    /// </summary>
    private async Task<string?> ChatJsonOrNullIfEmptyAfterRetryAsync(
        Func<Task<string>> callOnce,
        string stepLabel)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var text = await callOnce();
            if (!LlmJson.IsEmptyJsonObject(text))
                return text;
            if (attempt == 0)
                _logger.LogWarning("Model returned empty JSON object for {Step}; retrying once.", stepLabel);
        }

        _logger.LogWarning("Model returned empty JSON object for {Step} after retry; continuing with default verdict.", stepLabel);
        return null;
    }

    private async Task<bool> RunTransitionCheckAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        string stateBefore,
        string draft,
        string stateAfter,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You verify narrative continuity. Output ONLY JSON: { "pass": bool, "gaps": string[] }. Never output an empty object {}.
            stateAfter should be the merged end-state after the prose (start snapshot + changes from the scene). Check that the prose plausibly accounts for changes from stateBefore to stateAfter (environment, positions, dress, emotional shifts, who is present).
            List concrete gaps if the prose cannot support the delta, or if stateAfter drops continuity facts the prose still implies.
            Flag contradictions with established world-building or story tone when relevant.
            """
            + InventionScopeHardRule
            + """
            If the prose invents named characters, relationships, or major plot events not allowed by the synopsis, linked world-building, or prior state, treat that as a serious gap.
            """;
        var user = $"""
            stateBefore: {stateBefore}
            prose: {draft}
            stateAfter: {stateAfter}

            {worldContextBlock}
            """;
        var transitionOptions = new OllamaChatOptions { NumPredict = 2048 };
        var textOrNull = await ChatJsonOrNullIfEmptyAfterRetryAsync(
            async () =>
            {
                var (t, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.TransitionCheck, system, user,
                    jsonFormat: true, transitionOptions, cancellationToken: cancellationToken, progress,
                    "Continuity check (before / prose / after)");
                return t;
            },
            "transition check");
        var verdict = textOrNull is null
            ? new TransitionVerdict { Pass = true, Gaps = new List<string>() }
            : LlmJson.Deserialize<TransitionVerdict>(textOrNull);
        var pass = verdict?.Pass ?? true;
        await db.ComplianceEvaluations.AddAsync(new ComplianceEvaluation
        {
            Id = Guid.NewGuid(),
            GenerationRunId = run.Id,
            Passed = pass,
            Kind = "Transition",
            AttemptNumber = 0,
            VerdictJson = JsonSerializer.Serialize(verdict ?? new TransitionVerdict { Pass = true, Gaps = new List<string>() }),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return pass;
    }

    private static string BuildAuthorizedCastBlock(string stateBefore, IReadOnlyList<WorldElement> linkedElements, Scene scene) =>
        AuthorizedCastPromptBuilder.Build(stateBefore, linkedElements, SceneInstructionsForAgent(scene));

    private async Task<ComplianceVerdict> EvaluateComplianceAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string stateBefore,
        string draft,
        string worldContextBlock,
        string authorizedCastBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var raw = await EvaluateComplianceVerdictAsync(db, ollama, model, run.Id, scene, stateBefore, draft, worldContextBlock, authorizedCastBlock, progress, cancellationToken);
        var processed = AgentVerification.ProcessCompliance(draft, raw, BuildAgentGuardContext(scene, stateBefore));
        if (processed.DroppedItems.Count > 0)
            _logger.LogInformation("Compliance grounding dropped {Count} critic item(s) not evidenced in draft for run {RunId}",
                processed.DroppedItems.Count, run.Id);
        var verdict = processed.Verdict;
        await db.ComplianceEvaluations.AddAsync(new ComplianceEvaluation
        {
            Id = Guid.NewGuid(),
            GenerationRunId = run.Id,
            Passed = verdict.Pass,
            Kind = "Compliance",
            AttemptNumber = 0,
            VerdictJson = JsonSerializer.Serialize(verdict),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return verdict;
    }

    private async Task<ComplianceVerdict> EvaluateComplianceVerdictAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        Guid generationRunId,
        Scene scene,
        string stateBefore,
        string draft,
        string worldContextBlock,
        string authorizedCastBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You check instruction compliance. Output ONLY one JSON object, no markdown fences.
            Required shape (always include every key): { "pass": boolean, "violations": string[], "fixInstructions": string[] }.
            You MUST set "pass" explicitly to true or false. Never output an empty object {}.
            If there are no violations, use "pass": true and empty arrays: "violations": [], "fixInstructions": [].
            Violations: concrete issues in the draft section only — wrong ending vs scene instructions; invented named people/relationships/events with quoted draft evidence; ignored scene constraints; contradictions of linked world-building; POV/tense mismatch when specified; clear grammar/punctuation errors with quoted offending text; undue telling where the brief expects dramatization. Each violation MUST include a verbatim quote from the draft or a ¶ index plus quoted words. Do NOT output rule headings without draft evidence. Do NOT list characters as "unintroduced" or "invented" when they are named in the scene brief, listed in stateBefore, in linked world-building, or clearly introduced on-page earlier in the same draft.
            fixInstructions: minimal edits to fix issues while preserving plot, voice, and authorized facts; every fix MUST quote exact current draft text to locate the change; for voice mismatches, rewrite to the required perspective, POV, and tense; for grammar/punctuation, give the corrected wording using words from the draft.
            """
            + ComplianceCitationRule
            + ComplianceCheckerScope
            + ShowDontTellEmphasis
            + InventionScopeHardRule
            + """
            Treat any invented named character, relationship, or story event outside the scene brief, stateBefore, and linked world-building as a compliance failure — cite the invented name or beat with a draft quote. Do not fail based on book synopsis alone.
            """;
        var user = $"""
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective (required when not "(any)"): {scene.NarrativePerspective ?? "(any)"}
            Narrative tense (required when not "(any)"): {scene.NarrativeTense ?? "(any)"}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}
            stateBefore (continuity context — NOT the draft to check; do not quote stateBefore text as if it were draft prose): {stateBefore}

            {authorizedCastBlock}

            draft (ONLY this prose is under review — all violations and fixes must cite text from here):
            {draft}

            {worldContextBlock}
            """;
        var complianceOptions = new OllamaChatOptions { NumPredict = 2048 };
        var textOrNull = await ChatJsonOrNullIfEmptyAfterRetryAsync(
            async () =>
            {
                var (t, _, _) = await ChatAndLogForRunAsync(db, ollama, model, generationRunId, PipelineStep.Compliance, system, user,
                    jsonFormat: true, complianceOptions, cancellationToken: cancellationToken, progress,
                    "Compliance check");
                return t;
            },
            "instruction compliance");
        var raw = textOrNull is null
            ? new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() }
            : LlmJson.DeserializeComplianceVerdict(textOrNull);
        return raw;
    }

    private async Task<string> InvokeAgentWriterParagraphAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string stateBeforeJson,
        string worldContextBlock,
        AgentWriterInvokeRequest req,
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var proseOptions = CreateDraftProseOptions(Math.Max(1024, _ollamaOptions.Value.DraftNumPredict));
        var continuityBrief = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(stateBeforeJson);
        var system =
            """
            You rewrite a specific passage of fiction prose per the orchestrating editor's instruction.
            """
            + InventionScopeHardRule
            + ShowDontTellEmphasis
            + BeginningStateContinuityForProseRule
            + """
            Output ONLY the replacement prose for the passage identified in the user message (same paragraph count allowed via blank lines). No title, preamble, or explanation.
            Preserve all plot-critical events in the passage unless the instruction explicitly changes them.
            """;
        var user = $"""
            Narrative perspective: {scene.NarrativePerspective ?? "(infer from story)"}
            Narrative tense: {scene.NarrativeTense ?? "(infer from story)"}
            {continuityBrief}

            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}

            Full draft for context (replace ONLY paragraphs {req.ParagraphStart}..{req.ParagraphEnd} inclusive):
            ---
            {req.FullDraft}
            ---

            Passage to rewrite (paragraphs {req.ParagraphStart}..{req.ParagraphEnd}):
            ---
            {req.SpanText}
            ---
            {(req.TargetWords is > 0 ? $"Approximate word target for this passage: {req.TargetWords} words.\n" : "")}{FormatAgentDelegationScopeBlock(req)}

            Editor instruction for this passage:
            {req.Instruction}
            {FormatAgentDelegationComplianceBlock(req)}
            {FormatAgentDelegationQualityBlock(req)}

            {worldContextBlock}
            """;
        var (text, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.AgentEdit, system, user,
            jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            AgentDelegationWaitLabel("Writer", req), progressStep: "Writer");
        return DraftProseGuard.TrimRepetitiveLoops(LlmProseSanitizer.ProseForApplication(text).Trim());
    }

    private async Task<string> InvokeAgentCorrectorParagraphAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        AgentWriterInvokeRequest req,
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var options = new OllamaChatOptions { NumPredict = Math.Max(512, _ollamaOptions.Value.DraftNumPredict / 2) };
        var system =
            """
            You correct grammar, punctuation, and spelling in a fiction passage. You are NOT rewriting for style, plot, or voice.
            Preserve the author's word choice, rhythm, dialect, and intentional fragments unless they are objectively ungrammatical errors.
            Do not add, remove, or alter plot events, character names, or facts.
            Output ONLY the corrected replacement prose for the passage (blank lines may separate paragraphs). No title, preamble, or explanation.
            """;
        var user = $"""
            Narrative perspective: {scene.NarrativePerspective ?? "(preserve as written)"}
            Narrative tense: {scene.NarrativeTense ?? "(preserve as written)"}

            Full draft for context (correct ONLY paragraphs {req.ParagraphStart}..{req.ParagraphEnd} inclusive):
            ---
            {req.FullDraft}
            ---

            Passage to correct (paragraphs {req.ParagraphStart}..{req.ParagraphEnd}):
            ---
            {req.SpanText}
            ---
            {FormatAgentDelegationScopeBlock(req)}

            Correction focus from the editor (apply these fixes; change nothing else):
            {req.Instruction}
            {FormatAgentDelegationComplianceBlock(req)}
            {FormatAgentDelegationQualityBlock(req)}
            """;
        var (text, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.AgentEdit, system, user,
            jsonFormat: false, options, cancellationToken: cancellationToken, progress,
            AgentDelegationWaitLabel("Corrector", req), progressStep: "Corrector");
        return DraftProseGuard.TrimRepetitiveLoops(LlmProseSanitizer.ProseForApplication(text).Trim());
    }

    private async Task<string> InvokeAgentEditorParagraphAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string stateBeforeJson,
        string worldContextBlock,
        AgentWriterInvokeRequest req,
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var options = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict / 2) };
        var continuityBrief = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(stateBeforeJson);
        var system =
            """
            You lightly edit fiction prose per the orchestrating editor's instruction without significantly changing meaning, plot beats, or dramatized events.
            Typical tasks: convert tense or narrative perspective/POV as specified, apply markdown decoration when requested (*italic*, **bold**, etc.), align phrasing with scene constraints.
            Do NOT rewrite for creative improvement, add or remove plot events, change character facts, or invent new names.
            Preserve voice and substance unless the instruction explicitly requires tense or perspective conversion across the passage.
            Output ONLY the edited replacement prose (blank lines may separate paragraphs). No title, preamble, or explanation.
            """
            + InventionScopeHardRule;
        var user = $"""
            Target narrative perspective: {scene.NarrativePerspective ?? "(preserve unless instruction says otherwise)"}
            Target narrative tense: {scene.NarrativeTense ?? "(preserve unless instruction says otherwise)"}
            {continuityBrief}

            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}

            Full draft for context (edit ONLY paragraphs {req.ParagraphStart}..{req.ParagraphEnd} inclusive):
            ---
            {req.FullDraft}
            ---

            Passage to edit (paragraphs {req.ParagraphStart}..{req.ParagraphEnd}):
            ---
            {req.SpanText}
            ---
            {FormatAgentDelegationScopeBlock(req)}

            Editor instruction for this passage (address the author's or compliance fix request precisely):
            {req.Instruction}
            {FormatAgentDelegationComplianceBlock(req)}
            {FormatAgentDelegationQualityBlock(req)}

            {worldContextBlock}
            """;
        var (text, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.AgentEdit, system, user,
            jsonFormat: false, options, cancellationToken: cancellationToken, progress,
            AgentDelegationWaitLabel("Editor", req), progressStep: "Editor");
        return DraftProseGuard.TrimRepetitiveLoops(LlmProseSanitizer.ProseForApplication(text).Trim());
    }

    private async Task<QualityVerdict> EvaluateQualityVerdictAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        Guid generationRunId,
        Scene scene,
        string stateBefore,
        string draft,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system =
            """
            You critique prose quality for long-form fiction. Output ONLY JSON:
            { "score": number, "issues": string[], "fixInstructions": string[] }. Never output an empty object {}.
            score: one number from 0 (serious craft or scope problems) to 100 (strong craft for this brief). Use the full range; reserve the high 90s–100 for genuinely polished work.
            issues: concrete problem areas for the author (may be non-empty even when score is high — for manual review).
            fixInstructions: optional targeted craft fixes; may be empty when issues are minor.
            """
            + QualityCheckerScope
            + ShowDontTellEmphasis
            + InventionScopeHardRule
            + """
            Lower the score for prose that smuggles in new named characters, relationships, or plot events not grounded in the scene synopsis/instructions, expected end notes, state-before JSON, and linked world-building — not merely because the book-level synopsis elsewhere mentions different characters or future plot.
            Lower the score when the opening paragraphs read like a restated state-table inventory (pose, clothing, mood labels, blocking) instead of dramatized action or dialogue.
            Plot beats and facts that appear in the scene synopsis, instructions, expected end notes, or state-before JSON are authorized; do not treat them as inventions.
            fixInstructions: targeted craft rewrites only; preserve plot and compliance; do not suggest adding new characters or events outside scope; never sanitize for propriety.
            """;
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective: {scene.NarrativePerspective ?? "(any)"}
            Narrative tense: {scene.NarrativeTense ?? "(any)"}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}
            State before (JSON): {stateBefore}
            draft: {draft}

            {worldContextBlock}
            """;
        var qualityOptions = new OllamaChatOptions { NumPredict = 2048 };
        var textOrNull = await ChatJsonOrNullIfEmptyAfterRetryAsync(
            async () =>
            {
                var (t, _, _) = await ChatAndLogForRunAsync(db, ollama, model, generationRunId, PipelineStep.Quality, system, user,
                    jsonFormat: true, qualityOptions, cancellationToken: cancellationToken, progress,
                    "Prose quality critique");
                return t;
            },
            "prose quality");
        var verdict = textOrNull is null
            ? new QualityVerdict { Issues = new List<string>(), FixInstructions = new List<string>() }
            : LlmJson.Deserialize<QualityVerdict>(textOrNull)
              ?? new QualityVerdict { Issues = new List<string>(), FixInstructions = new List<string>() };
        verdict = NormalizeQualityVerdict(verdict);
        return AgentVerification.ProcessQuality(draft, verdict, BuildAgentGuardContext(scene, stateBefore));
    }

    private (int MinWords, int MaxWords) ResolveSessionWordTargets(GenerationRun run) =>
        ResolveSessionWordTargetsFromOverrides(run.MinWordsOverride, run.MaxWordsOverride);

    private (int MinWords, int MaxWords) ResolveSessionWordTargetsFromOverrides(int? minOverride, int? maxOverride)
    {
        var minWords = Math.Max(100, minOverride ?? _ollamaOptions.Value.DraftMinWords);
        var defaultMax = Math.Min(2000, Math.Max(minWords, 1500));
        var maxTarget = maxOverride ?? defaultMax;
        if (maxTarget < minWords)
            maxTarget = minWords;
        return (minWords, maxTarget);
    }

    private static string BuildRemediationMission(ComplianceVerdict compliance, QualityVerdict? quality, double reviewMin)
    {
        var sb = new StringBuilder();
        sb.AppendLine("REMEDIATION PASS — the pipeline terminal gate failed after the initial agent session.");
        sb.AppendLine("Fix every outstanding item below. Use check_scene_brief, read_section, find_text, then targeted edits.");
        if (!compliance.Pass)
        {
            sb.AppendLine();
            sb.AppendLine("Compliance failures:");
            foreach (var v in compliance.Violations)
                sb.AppendLine($"  • {v}");
            foreach (var f in compliance.FixInstructions)
                sb.AppendLine($"  → {f}");
        }

        if (quality is not null && (quality.Score ?? 0) < reviewMin)
        {
            sb.AppendLine();
            sb.AppendLine($"Quality score {quality.Score:0} (need ≥{reviewMin:0}):");
            foreach (var issue in quality.Issues)
                sb.AppendLine($"  • {issue}");
            foreach (var f in quality.FixInstructions)
                sb.AppendLine($"  → {f}");
        }

        return sb.ToString().TrimEnd();
    }

    private static AgentDeterministicGuards.GuardContext BuildAgentGuardContext(Scene scene, string stateBeforeJson) =>
        new(scene.NarrativePerspective, scene.NarrativeTense, scene.ExpectedEndStateNotes, stateBeforeJson);

    private AgentSessionDelegates BuildAgentDelegates(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string writer,
        string critic,
        string qualityCritic,
        string correctionModel,
        string editor,
        GenerationRun run,
        Scene scene,
        string stateBeforeJson,
        string worldBlock,
        string authorizedCastBlock,
        PipelineProgress? progress) =>
        new()
        {
            RunComplianceAsync = (draftText, ct) =>
                EvaluateComplianceVerdictAsync(db, ollama, critic, run.Id, scene, stateBeforeJson, draftText, worldBlock,
                    authorizedCastBlock, progress, ct),
            RunQualityAsync = (draftText, ct) =>
                EvaluateQualityVerdictAsync(db, ollama, qualityCritic, run.Id, scene, stateBeforeJson, draftText, worldBlock,
                    progress, ct),
            InvokeWriterAsync = (req, ct) =>
                InvokeAgentWriterParagraphAsync(db, ollama, writer, run, scene, stateBeforeJson, worldBlock, req, progress, ct),
            InvokeCorrectorAsync = (req, ct) =>
                InvokeAgentCorrectorParagraphAsync(db, ollama, correctionModel, run, scene, req, progress, ct),
            InvokeEditorAsync = (req, ct) =>
                InvokeAgentEditorParagraphAsync(db, ollama, editor, run, scene, stateBeforeJson, worldBlock, req, progress, ct)
        };

    private async Task<QualityVerdict> EvaluateQualityAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string stateBefore,
        string draft,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var verdict = await EvaluateQualityVerdictAsync(db, ollama, model, run.Id, scene, stateBefore, draft, worldContextBlock,
            progress, cancellationToken);
        var (reviewMin, _) = GetQualityScoreThresholds(run);
        await db.ComplianceEvaluations.AddAsync(new ComplianceEvaluation
        {
            Id = Guid.NewGuid(),
            GenerationRunId = run.Id,
            Passed = verdict.Score >= reviewMin,
            Kind = "Quality",
            AttemptNumber = 0,
            VerdictJson = JsonSerializer.Serialize(verdict),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return verdict;
    }

    /// <summary>Clamps score to 0–100; infers score from legacy <c>pass</c> when missing.</summary>
    private static QualityVerdict NormalizeQualityVerdict(QualityVerdict verdict)
    {
        verdict.Issues ??= new List<string>();
        verdict.Issues = verdict.Issues.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        verdict.FixInstructions ??= new List<string>();
        verdict.FixInstructions = verdict.FixInstructions.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

        double score;
        if (verdict.Score is { } raw && !double.IsNaN(raw) && !double.IsInfinity(raw))
            score = Math.Clamp(raw, 0, 100);
        else if (verdict.Pass == true)
            score = 82;
        else if (verdict.Pass == false)
            score = 42;
        else
            score = verdict.Issues.Count == 0 ? 78 : 62;

        verdict.Score = score;
        return verdict;
    }

    /// <summary>
    /// Snapshots effective quality thresholds on the run (request overrides, then Ollama config).
    /// </summary>
    private string EffectiveCorrectionModel(string critic) =>
        !string.IsNullOrWhiteSpace(_ollamaOptions.Value.CorrectionModel)
            ? _ollamaOptions.Value.CorrectionModel.Trim()
            : critic;

    private static string FormatAgentDelegationComplianceBlock(AgentWriterInvokeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ComplianceContext))
            return "";
        return $"""

            Compliance context (mandatory — address every item that applies to this passage):
            {req.ComplianceContext.Trim()}
            """;
    }

    private static string FormatAgentDelegationQualityBlock(AgentWriterInvokeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.QualityContext))
            return "";
        return $"""

            Quality craft context (address items that apply to this passage):
            {req.QualityContext.Trim()}
            """;
    }

    private static string FormatAgentDelegationScopeBlock(AgentWriterInvokeRequest req)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(req.ContextBeforeText))
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Surrounding context BEFORE (paragraphs {req.ParagraphStart - req.ContextParagraphsBefore}..{req.ParagraphStart - 1} — reference only, do NOT reproduce in output):");
            sb.AppendLine("---");
            sb.AppendLine(req.ContextBeforeText.Trim());
            sb.AppendLine("---");
        }

        if (!string.IsNullOrWhiteSpace(req.ContextAfterText))
        {
            sb.AppendLine();
            sb.AppendLine(
                $"Surrounding context AFTER (paragraphs {req.ParagraphEnd + 1}..{req.ParagraphEnd + req.ContextParagraphsAfter} — reference only, do NOT reproduce in output):");
            sb.AppendLine("---");
            sb.AppendLine(req.ContextAfterText.Trim());
            sb.AppendLine("---");
        }

        if (!string.IsNullOrWhiteSpace(req.FocusExcerpt))
        {
            sb.AppendLine();
            sb.AppendLine("PRIMARY FOCUS within the passage below (apply the instruction mainly here; keep the rest of the passage unchanged unless the instruction requires broader edits):");
            sb.AppendLine("---");
            sb.AppendLine(req.FocusExcerpt.Trim());
            sb.AppendLine("---");
        }

        return sb.ToString();
    }

    private static void ApplyQualityThresholdsToRun(GenerationRun run, GenerationStartOptions? options, OllamaOptions config)
    {
        var accept = options?.QualityAcceptMinScore ?? config.QualityAcceptMinScore;
        var review = options?.QualityReviewOnlyMinScore ?? config.QualityReviewOnlyMinScore;
        run.QualityAcceptMinScore = Math.Clamp(accept, 0, 100);
        run.QualityReviewOnlyMinScore = Math.Clamp(review, 0, 100);
    }

    /// <summary>
    /// Ensures review floor ≤ accept line; both clamped to 0–100.
    /// </summary>
    private static (double ReviewMin, double AcceptMin) GetQualityScoreThresholds(GenerationRun run)
    {
        return GetQualityScoreThresholds(run.QualityAcceptMinScore, run.QualityReviewOnlyMinScore);
    }

    private static (double ReviewMin, double AcceptMin) GetQualityScoreThresholds(double acceptMin, double reviewMin)
    {
        var accept = Math.Clamp(acceptMin, 0, 100);
        var review = Math.Clamp(reviewMin, 0, 100);
        if (review > accept)
            (review, accept) = (accept, review);
        return (review, accept);
    }

    private static string BuildComplianceIssuesOnlyDetail(ComplianceVerdict v)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Compliance: issues found. The draft was not auto-revised — edit in review or regenerate.");
        sb.AppendLine("Violations:");
        if (v.Violations.Count == 0)
            sb.AppendLine("  (none listed)");
        else
            foreach (var x in v.Violations)
                sb.AppendLine($"  • {x}");
        if (v.FixInstructions.Count > 0)
        {
            sb.AppendLine("Suggested fixes (for you to apply):");
            foreach (var x in v.FixInstructions)
                sb.AppendLine($"  • {x}");
        }

        return sb.ToString();
    }

    private static string BuildQualityScoreNoteDetail(QualityVerdict v, double score, double reviewMin, double acceptMin)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"Quality score: {score:0.#} (below pipeline pass threshold {reviewMin:0.#}). The draft was not auto-revised.");
        sb.AppendLine($"Bands: pass with review ≥{reviewMin:0.#}; no automated repair ≥{acceptMin:0.#}.");
        if (v.Issues.Count > 0)
        {
            sb.AppendLine("Issues:");
            foreach (var x in v.Issues)
                sb.AppendLine($"  • {x}");
        }

        if (v.FixInstructions.Count > 0)
        {
            sb.AppendLine("Suggested craft fixes (optional):");
            foreach (var x in v.FixInstructions)
                sb.AppendLine($"  • {x}");
        }

        return sb.ToString();
    }

    private async Task<string> EnsureUsableStateJsonAsync(
        Func<Task<string>> generateOnce,
        string? fallbackJson,
        string stepLabel,
        CancellationToken cancellationToken,
        string? failureMessage = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var json = (await generateOnce()).Trim();
            if (LlmJson.TryNormalizeStateJson(json, out var normalized))
                return normalized;
            if (attempt == 0)
                _logger.LogWarning("{Step} returned empty or invalid JSON; retrying once.", stepLabel);
        }

        var fallback = fallbackJson?.Trim();
        if (LlmJson.TryNormalizeStateJson(fallback, out var fallbackNormalized))
        {
            _logger.LogWarning("{Step} returned invalid JSON after retry; using continuity fallback.", stepLabel);
            return fallbackNormalized;
        }

        throw new InvalidOperationException(
            failureMessage ??
            $"{stepLabel} produced empty JSON and no usable continuity fallback is available. " +
            "Ensure Ollama is running and Settings → Ollama models has a pre-state model configured.");
    }

    private static async Task<string?> ResolvePostStateFallbackAsync(
        ICreativeLongformDbContext db,
        Guid generationRunId,
        Scene scene,
        string? stateBeforeJson,
        CancellationToken cancellationToken)
    {
        var runSnapshot = await db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == generationRunId && s.Step == PipelineStep.PostState)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => s.StateJson)
            .FirstOrDefaultAsync(cancellationToken);

        return LlmJson.FirstUsableStateJson(scene.PendingPostStateJson, runSnapshot, stateBeforeJson);
    }

    private async Task SaveSnapshotAsync(
        ICreativeLongformDbContext db,
        Guid runId,
        PipelineStep step,
        string stateJson,
        CancellationToken cancellationToken)
    {
        await db.StateSnapshots.AddAsync(new StateSnapshot
        {
            Id = Guid.NewGuid(),
            GenerationRunId = runId,
            Step = step,
            SchemaVersion = 1,
            StateJson = stateJson,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string messageText, string rawResponse, Guid llmCallId)> ChatAndLogAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        LlmAuditContext audit,
        PipelineStep step,
        string system,
        string user,
        bool jsonFormat,
        OllamaChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default,
        PipelineProgress? progress = null,
        string? progressSummary = null,
        string? progressStep = null)
    {
        if (audit.GenerationRunId is null && audit.BookId is null)
            throw new ArgumentException("Either GenerationRunId or BookId is required for LLM audit logging.");

        var signalStep = progressStep ?? step.ToString();
        var messages = new List<OllamaChatMessage>
        {
            new("system", system),
            new("user", user)
        };
        var req = JsonSerializer.Serialize(new
        {
            model,
            messages,
            format = jsonFormat ? "json" : (string?)null,
            num_predict = chatOptions?.NumPredict
        });
        var llmCallId = Guid.NewGuid();
        if (progress is not null && audit.GenerationRunId is Guid progressRunIdForStart)
        {
            var startLabel = progressSummary ?? step.ToString();
            await progress.Notifier.NotifyAsync(progressRunIdForStart, "LlmStarted", signalStep,
                $"{startLabel}: streaming from model «{model}»…",
                cancellationToken,
                progress.ElapsedMs(),
                null,
                llmCallId);
        }

        var streamBatch = new StringBuilder();
        var streamLock = new object();
        var lastStreamFlush = Stopwatch.StartNew();
        Func<OllamaStreamUpdate, CancellationToken, Task>? onStreamUpdate = null;
        if (progress is not null && audit.GenerationRunId is Guid progressRunIdForStream)
        {
            onStreamUpdate = async (update, ct) =>
            {
                if (string.IsNullOrEmpty(update.Delta) && !update.Done)
                    return;

                lock (streamLock)
                {
                    if (!string.IsNullOrEmpty(update.Delta))
                        streamBatch.Append(update.Delta);
                }

                var shouldFlush = update.Done ||
                                  lastStreamFlush.ElapsedMilliseconds >= 200 ||
                                  streamBatch.Length >= 512;
                if (!shouldFlush)
                    return;

                string delta;
                lock (streamLock)
                {
                    if (streamBatch.Length == 0)
                        return;
                    delta = streamBatch.ToString();
                    streamBatch.Clear();
                    lastStreamFlush.Restart();
                }

                await progress.Notifier.NotifyAsync(progressRunIdForStream, "LlmStreamChunk", signalStep,
                    delta, ct, progress.ElapsedMs(), null, llmCallId);
            };
        }

        var roundSw = Stopwatch.StartNew();
        var result = await ollama.ChatAsync(
            model, messages, jsonFormat, chatOptions, onStreamUpdate, cancellationToken);
        roundSw.Stop();
        await db.LlmCalls.AddAsync(new LlmCall
        {
            Id = llmCallId,
            GenerationRunId = audit.GenerationRunId,
            BookId = audit.BookId,
            Step = step,
            Model = model,
            RequestJson = req,
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (progress is not null && audit.GenerationRunId is Guid progressRunId)
        {
            var label = progressSummary ?? step.ToString();
            await progress.Notifier.NotifyAsync(progressRunId, "LlmRoundtrip", signalStep,
                $"{label}: model «{model}» returned {result.MessageText.Length:N0} characters in {roundSw.ElapsedMilliseconds} ms.",
                cancellationToken,
                progress.ElapsedMs(),
                roundSw.ElapsedMilliseconds,
                llmCallId);
        }

        return (result.MessageText, result.MessageText, llmCallId);
    }

    private async Task<(string messageText, string rawResponse, Guid llmCallId)> ChatAndLogForRunAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        Guid runId,
        PipelineStep step,
        string system,
        string user,
        bool jsonFormat,
        OllamaChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default,
        PipelineProgress? progress = null,
        string? progressSummary = null,
        string? progressStep = null)
        => await ChatAndLogAsync(db, ollama, model, LlmAuditContext.ForRun(runId), step, system, user, jsonFormat,
            chatOptions, cancellationToken, progress, progressSummary, progressStep);
}
