using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
using CreativeLongform.Application.Narrative;
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
        Only evaluate craft: show vs tell, metaphor clarity, on-the-nose labels, flat exposition where dramatization fits, perspective/tense consistency with the brief, and accidental invention of NEW named characters or plot beats not grounded in the scene synopsis/instructions and linked world-building (not the book-level synopsis alone).
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
        Do NOT fail because the draft omits characters, subplots, or future book-level beats that appear only in the book synopsis line (series overview) but are not required by this scene’s synopsis/instructions, linked elements, or state. The book synopsis is mood and continuity context, not a per-scene requirement list.
        Do NOT fail because the scene draft is a narrow slice of the book synopsis — scenes are allowed to be partial.
        If the scene synopsis reads like an outline or mentions ideas for later chapters, treat those as guidance for this scene only where they clearly apply; do not require every outline bullet to appear as prose.
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
        return _cancellationRegistry.TryCancel(generationRunId);
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

        await notifier.NotifyAsync(generationRunId, "StepStarted", nameof(PipelineStep.Draft),
            $"Correcting draft with model «{writer}» (user instruction).", cancellationToken,
            correctSw.ElapsedMilliseconds, null, null);
        var text = await RepairDraftWithUserInstructionAsync(db, ollama, writer, run, scene, draft, ins, worldBlock,
            stateBeforeJson, selectionStart, selectionEnd, cancellationToken, correctProgress);
        text = ApplyLlmDraftFromModel(scene, text);
        text = GuardDraftProse(text, generationRunId, "correct draft", stateBeforeJson);
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
        catch (OperationCanceledException)
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

            await NotifyStepAsync(notifier, runId, PipelineStep.Draft, progress.ElapsedMs,
                $"Draft: asking model «{writer}» to produce the scene prose.", cancellationToken);
            var draft = await GenerateDraftAsync(db, ollama, writer, run, scene, stateBefore, worldBlock, minWords, maxTargetWords, progress, cancellationToken);

            if (_ollamaOptions.Value.AgenticEditEnabled && _ollamaOptions.Value.AgenticEditMaxTurns > 0)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.AgentEdit, progress.ElapsedMs,
                    "Agent edit: iterative tool loop (read sections, patches, finish) to refine the draft.", cancellationToken);
                var agentTurns = Math.Max(1, _ollamaOptions.Value.AgenticEditMaxTurns);
                var agentPredict = Math.Max(512, _ollamaOptions.Value.AgenticEditNumPredict);
                draft = await AgenticEditLoop.RunAsync(
                    draft,
                    SceneInstructionsForAgent(scene),
                    scene.ExpectedEndStateNotes,
                    worldBlock,
                    agentTurns,
                    _logger,
                    async (system, user, ct) =>
                    {
                        var o = new OllamaChatOptions { NumPredict = agentPredict };
                        return await ChatAndLogForRunAsync(db, ollama, agentModel, run.Id, PipelineStep.AgentEdit, system, user,
                            jsonFormat: true, o, ct, progress, "Agent edit turn (JSON tools)");
                    },
                    notifier,
                    runId,
                    progress.ElapsedMs,
                    cancellationToken);
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

            await NotifyStepAsync(notifier, runId, PipelineStep.Compliance, progress.ElapsedMs,
                "Compliance: checking the draft against scene instructions and world context.", cancellationToken);
            var lastCompliance = await EvaluateComplianceAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
            if (!lastCompliance.Pass)
            {
                await notifier.NotifyAsync(runId, "DraftReviewNote", PipelineStep.Compliance.ToString(),
                    BuildComplianceIssuesOnlyDetail(lastCompliance),
                    cancellationToken, progress.ElapsedMs(), null, null);
            }

            QualityVerdict? lastQuality = null;
            if (!run.SkipQualityGate)
            {
                var (reviewMin, acceptMin) = GetQualityScoreThresholds(run);
                await NotifyStepAsync(notifier, runId, PipelineStep.Quality, progress.ElapsedMs,
                    $"Quality: numeric prose score (pass ≥{reviewMin:0.#}; no automated repair ≥{acceptMin:0.#}).", cancellationToken);
                lastQuality = await EvaluateQualityAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
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

            run.FinalDraftText = text;
            scene.LatestDraftText = text;
            await db.SaveChangesAsync(cancellationToken);

            if (run.StopAfterDraft)
            {
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
                run.Status = GenerationRunStatus.Succeeded;
                run.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await notifier.NotifyAsync(runId, "RunFinished", "Succeeded",
                    "Pipeline completed; scene and state snapshots saved.", cancellationToken, progress.ElapsedMs(), null, null);
            }
        }
        catch (OperationCanceledException)
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

    private async Task PersistCancelledRunAsync(Guid runId, Stopwatch pipelineSw)
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
            "Generation was cancelled.", CancellationToken.None, pipelineSw.ElapsedMilliseconds, null, null);
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

    private async Task<ComplianceVerdict> EvaluateComplianceAsync(
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
        var system =
            """
            You check instruction compliance. Output ONLY one JSON object, no markdown fences.
            Required shape (always include every key): { "pass": boolean, "violations": string[], "fixInstructions": string[] }.
            You MUST set "pass" explicitly to true or false. Never output an empty object {}.
            If there are no violations, use "pass": true and empty arrays: "violations": [], "fixInstructions": [].
            Violations: wrong ending vs scene instructions, invented characters or relationships or plot events not grounded in the scene synopsis/instructions (below), stateBefore, and linked world-building — not in the book-level synopsis alone. Ignored scene constraints, contradictions of linked world-building, undue telling or labeled emotion where the brief allows dramatization (show, don't tell).
            fixInstructions: minimal edits to fix issues while preserving voice.
            """
            + ComplianceCheckerScope
            + ShowDontTellEmphasis
            + InventionScopeHardRule
            + """
            Treat any invented named character, relationship, or story event outside the scene brief, stateBefore, and linked world-building as a compliance failure — not merely because the book synopsis elsewhere mentions different characters or future plot.
            """;
        var user = $"""
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}
            stateBefore: {stateBefore}
            draft: {draft}

            {worldContextBlock}
            """;
        var complianceOptions = new OllamaChatOptions { NumPredict = 2048 };
        var textOrNull = await ChatJsonOrNullIfEmptyAfterRetryAsync(
            async () =>
            {
                var (t, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.Compliance, system, user,
                    jsonFormat: true, complianceOptions, cancellationToken: cancellationToken, progress,
                    "Instruction compliance check");
                return t;
            },
            "instruction compliance");
        var verdict = textOrNull is null
            ? new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() }
            : LlmJson.DeserializeComplianceVerdict(textOrNull);
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
                var (t, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.Quality, system, user,
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

    private async Task<string> RepairDraftWithUserInstructionAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string draft,
        string userInstruction,
        string worldContextBlock,
        string stateBeforeJson,
        int? selectionStart,
        int? selectionEnd,
        CancellationToken cancellationToken,
        PipelineProgress? progress = null)
    {
        var proseOptions = CreateDraftProseOptions(Math.Max(1024, _ollamaOptions.Value.DraftNumPredict));
        var perspective = scene.NarrativePerspective ?? "(infer from story tone if not specified)";
        var tense = scene.NarrativeTense ?? "(infer from story tone if not specified)";
        var continuityBrief = NarrativeStateContinuityBriefBuilder.BuildForDraftPrompt(stateBeforeJson);
        var sceneBlock = $"""
            Narrative perspective (follow strictly): {perspective}
            Narrative tense (follow strictly): {tense}

            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}
            {continuityBrief}
            """;

        if (selectionStart is int start && selectionEnd is int end && end > start)
        {
            var selected = draft[start..end];
            var system =
                """
                You replace one selected passage of fiction prose according to the author's instruction. The user message includes the full draft for context only.
                """
                + InventionScopeHardRule
                + ShowDontTellEmphasis
                + BeginningStateContinuityForProseRule
                + """
                OUTPUT FORMAT — output ONLY valid JSON: {"replacement":"..."}. The "replacement" string is the new prose for the selected passage only (not the whole scene). Match voice, tense, and perspective of the surrounding draft. No markdown fences, no extra keys, no explanation.
                """;
            var user = $"""
                [SELECTION MODE — UTF-16 indices {start}..{end} exclusive]

                {sceneBlock}

                Full draft for context (do not output this in full — only the JSON replacement field):
                ---
                {draft}
                ---

                Selected passage to replace (verbatim from the draft):
                ---
                {selected}
                ---

                Author instruction (applies to the selected passage only):
                {userInstruction}

                {worldContextBlock}
                """;
            var (text, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user,
                jsonFormat: true, proseOptions, cancellationToken: cancellationToken, progress,
                "Correct draft (selection replacement JSON)");
            var parsed = LlmJson.Deserialize<DraftReplacementJson>(text)
                         ?? throw new InvalidOperationException("Model did not return valid replacement JSON.");
            var replacement = parsed.Replacement ?? "";
            return draft[..start] + replacement + draft[end..];
        }

        var systemFull =
            """
            You revise fiction prose according to the author's explicit instructions. Preserve continuity and voice unless the author asks otherwise.
            """
            + InventionScopeHardRule
            + ShowDontTellEmphasis
            + BeginningStateContinuityForProseRule
            + """
            Output prose only, no preamble.
            """;
        var userFull = $"""
            {sceneBlock}

            Author instruction:
            {userInstruction}

            Current draft (revise as a whole per instruction):
            {draft}

            {worldContextBlock}
            """;
        var (textFull, _, _) = await ChatAndLogForRunAsync(db, ollama, model, run.Id, PipelineStep.Repair, systemFull, userFull,
            jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Correct draft (full revision)");
        return textFull.Trim();
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
        string? progressSummary = null)
    {
        if (audit.GenerationRunId is null && audit.BookId is null)
            throw new ArgumentException("Either GenerationRunId or BookId is required for LLM audit logging.");

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
        var roundSw = Stopwatch.StartNew();
        var result = await ollama.ChatAsync(model, messages, jsonFormat, chatOptions, cancellationToken);
        roundSw.Stop();
        var llmCallId = Guid.NewGuid();
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
            await progress.Notifier.NotifyAsync(progressRunId, "LlmRoundtrip", step.ToString(),
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
        string? progressSummary = null)
        => await ChatAndLogAsync(db, ollama, model, LlmAuditContext.ForRun(runId), step, system, user, jsonFormat,
            chatOptions, cancellationToken, progress, progressSummary);
}
