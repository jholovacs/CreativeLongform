using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Net.Http;
using System.Text.Json;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Generation;
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

    private sealed record PipelineProgress(IGenerationProgressNotifier Notifier, Func<long> ElapsedMs);

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
        var draft = (acceptedDraftText ?? run.FinalDraftText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(draft))
            throw new InvalidOperationException("No draft text to finalize.");

        var preSnap = await db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == generationRunId && s.Step == PipelineStep.PreState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var stateBefore = ResolveStateBeforeJsonForRun(preSnap?.StateJson, scene.BeginningStateJson);

        string stateAfter;
        if (!string.IsNullOrWhiteSpace(approvedStateTableJson))
        {
            stateAfter = approvedStateTableJson.Trim();
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }
        else
        {
            await NotifyStepAsync(notifier, generationRunId, PipelineStep.PostState, finalizeProgress.ElapsedMs,
                "Finalize: deriving post-scene state from accepted prose (merged from scene start state).", cancellationToken);
            stateAfter = await GeneratePostStateAsync(db, ollama, postStateModel, run, scene, stateBefore, draft, worldBlock, finalizeProgress, cancellationToken);
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }

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
        if (!scene.Chapter.IsComplete)
        {
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
            else
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
        run.FinalDraftText = text;
        scene.LatestDraftText = text;
        await db.SaveChangesAsync(cancellationToken);

        var postState = await GeneratePostStateAsync(db, ollama, postStateModel, run, scene, stateBeforeJson, text, worldBlock, progress: null, cancellationToken);
        await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, postState, cancellationToken);
        scene.PendingPostStateJson = postState;
        await db.SaveChangesAsync(cancellationToken);
        return new CorrectDraftResult(text, postState);
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
                        return await ChatAndLogAsync(db, ollama, agentModel, run.Id, PipelineStep.AgentEdit, system, user,
                            jsonFormat: true, o, ct, progress, "Agent edit turn (JSON tools)");
                    },
                    notifier,
                    runId,
                    progress.ElapsedMs,
                    cancellationToken);
            }

            if (!run.StopAfterDraft)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.PostState, progress.ElapsedMs,
                    "Post-state: deriving narrative state from the finished prose.", cancellationToken);
                var stateAfter = await GeneratePostStateAsync(db, ollama, postStateModel, run, scene, stateBefore, draft, worldBlock, progress, cancellationToken);
                await SaveSnapshotAsync(db, runId, PipelineStep.PostState, stateAfter, cancellationToken);

                await NotifyStepAsync(notifier, runId, PipelineStep.TransitionCheck, progress.ElapsedMs,
                    "Transition check: verifying continuity before → prose → after.", cancellationToken);
                var transitionOk = await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, progress, cancellationToken);
                if (!transitionOk)
                    _logger.LogWarning("Transition check reported gaps for run {RunId}", runId);
            }

            var text = draft;

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
                var postStateForReview = await GeneratePostStateAsync(db, ollama, postStateModel, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
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
    {
        var prevRun = await db.GenerationRuns
            .AsNoTracking()
            .Where(r => r.SceneId == sceneId && r.Status == GenerationRunStatus.Succeeded)
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

    private static async Task<string?> GetPreviousSceneLastPostStateJsonAsync(
        ICreativeLongformDbContext db,
        Guid sceneId,
        CancellationToken cancellationToken)
    {
        var scene = await db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .FirstAsync(s => s.Id == sceneId, cancellationToken);
        var bookId = scene.Chapter.BookId;
        var orderedIds = await db.Scenes.AsNoTracking()
            .Include(s => s.Chapter)
            .Where(s => s.Chapter.BookId == bookId)
            .OrderBy(s => s.Chapter.Order).ThenBy(s => s.Order)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        var idx = orderedIds.IndexOf(sceneId);
        if (idx <= 0)
            return null;
        var prevSceneId = orderedIds[idx - 1];
        return await GetLastSucceededPostStateJsonForSceneAsync(db, prevSceneId, cancellationToken);
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
        return await GeneratePreStateAsync(db, ollama, preStateModel, run, scene, sameScenePrior, worldBlock, progress, cancellationToken);
    }

    /// <summary>Prefer the run’s pre-state snapshot; if missing or empty, use author beginning-state JSON on the scene.</summary>
    private static string ResolveStateBeforeJsonForRun(string? preStateSnapshotJson, string? sceneBeginningStateJson)
    {
        var fromRun = preStateSnapshotJson?.Trim();
        if (!string.IsNullOrEmpty(fromRun) && fromRun != "{}")
            return fromRun;
        var author = sceneBeginningStateJson?.Trim();
        if (!string.IsNullOrEmpty(author) && author != "{}")
            return author;
        return "{}";
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
        GenerationRun run,
        Scene scene,
        string? priorStateJson,
        string worldContextBlock,
        PipelineProgress progress,
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
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PreState, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
            "Infer beginning narrative state (JSON)");
        return LlmJson.StripMarkdownFences(text);
    }

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
        var proseOptions = new OllamaChatOptions { NumPredict = numPredict };

        var system =
            """
            You are a fiction writer producing long-form prose for novels and serial fiction.
            """
            + InventionScopeHardRule
            + ShowDontTellEmphasis
            + """
            Follow the scene synopsis, instructions, and the established narrative state.
            Honor the requested narrative perspective and tense exactly.
            Write vivid prose; avoid naming character traits explicitly when a bio already labels them—show through action and detail.
            Respect story tone and linked world-building; do not invent facts that contradict them.
            Cast and world scope: only include or reference characters and world elements (people, places, factions, objects, lore)
            that appear under "Linked world-building" in the user message, or are explicitly named in the scene synopsis/instructions,
            or appear in the state-before JSON. Do not name or reference characters or world elements from the book synopsis,
            book-level notes, or the wider story unless they are covered by those sources—avoid importing the broader cast or canon.
            Develop the scene with multiple paragraphs: setting, action, dialogue, and character interiority as fits the brief.
            Do not stop after a few sentences; this is a full scene beat, not a summary.
            Output prose only, no preamble or title line.
            """;
        var user = $"""
            State before (JSON): {stateBeforeJson}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective: {scene.NarrativePerspective ?? "(infer from story)"}
            Narrative tense: {scene.NarrativeTense ?? "(infer from story)"}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

            {worldContextBlock}

            Write the complete scene. Target roughly {minWords}–{maxTargetWords} words for this session unless the brief explicitly demands a shorter piece.
            """;
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Draft, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Write scene draft (prose)");
        text = text.Trim();

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
                + """
                Add substantive prose—new paragraphs, beats, dialogue, sensory detail—not repetition of the same lines.
                Do not summarize the scene; extend it. Output prose only, no preamble.
                Do not introduce new characters, relationships, or plot events that are not already in the draft or grounded in Linked world-building
                and the scene synopsis/instructions in the user message.
                """;
            var expandUser = $"""
                The draft below is too short for this novel scene. It must reach at least {minWords} words total.
                Scene synopsis and instructions:
                {SceneInstructionsForAgent(scene)}
                Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

                {worldContextBlock}

                Expand and continue from the end of the draft below (you may revise transitions so it reads as one scene):
                {text}
                """;
            var (expanded, _, _) = await ChatAndLogAsync(
                db, ollama, model, run.Id, PipelineStep.Draft, expandSystem, expandUser, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
                "Expand short draft to target length");
            text = expanded.Trim();
        }

        return text;
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
        GenerationRun run,
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
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from prose if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from prose if not specified)"}

            State at scene ENTRY (JSON — same shape as beginning-state; baseline to merge forward from):
            {stateBeforeJson}

            Scene prose (this scene only — read facts from this text into the end state; this block is not JSON):
            {draftText}

            {worldContextBlock}

            Produce post-scene state only: infer the **end** snapshot in the **same format, field completeness, and concrete style** as you would for beginning-state at scene open — but every field must reflect the **last moment after** the prose above. Output JSON only.
            """;
        var postStatePredict = new OllamaChatOptions { NumPredict = Math.Max(2048, _ollamaOptions.Value.DraftNumPredict) };
        var (text, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PostState, system, user, jsonFormat: true, postStatePredict, cancellationToken: cancellationToken, progress,
            "Derive post-scene narrative state (JSON, merged from pre-state)");
        return LlmJson.StripMarkdownFences(text);
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
                var (t, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.TransitionCheck, system, user,
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
                var (t, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Compliance, system, user,
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
                var (t, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Quality, system, user,
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
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var perspective = scene.NarrativePerspective ?? "(infer from story tone if not specified)";
        var tense = scene.NarrativeTense ?? "(infer from story tone if not specified)";
        var sceneBlock = $"""
            Narrative perspective (follow strictly): {perspective}
            Narrative tense (follow strictly): {tense}

            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}
            State before (JSON): {stateBeforeJson}
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
            var (text, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user,
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
        var (textFull, _, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, systemFull, userFull,
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
        Guid runId,
        PipelineStep step,
        string system,
        string user,
        bool jsonFormat,
        OllamaChatOptions? chatOptions = null,
        CancellationToken cancellationToken = default,
        PipelineProgress? progress = null,
        string? progressSummary = null)
    {
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
            GenerationRunId = runId,
            BookId = null,
            Step = step,
            Model = model,
            RequestJson = req,
            ResponseText = result.MessageText,
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (progress != null)
        {
            var label = progressSummary ?? step.ToString();
            await progress.Notifier.NotifyAsync(runId, "LlmRoundtrip", step.ToString(),
                $"{label}: model «{model}» returned {result.MessageText.Length:N0} characters in {roundSw.ElapsedMilliseconds} ms.",
                cancellationToken,
                progress.ElapsedMs(),
                roundSw.ElapsedMilliseconds,
                llmCallId);
        }

        return (result.MessageText, result.MessageText, llmCallId);
    }
}
