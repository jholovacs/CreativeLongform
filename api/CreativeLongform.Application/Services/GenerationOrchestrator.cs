using System.Diagnostics;
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
            MinWordsOverride = options?.MinWordsOverride
        };
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
        var writer = _ollamaOptions.Value.WriterModel;
        var critic = _ollamaOptions.Value.CriticModel;
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
        var worldBlock = WorldContextBuilder.Build(book, worldElements);
        var draft = (acceptedDraftText ?? run.FinalDraftText ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(draft))
            throw new InvalidOperationException("No draft text to finalize.");

        string stateAfter;
        if (!string.IsNullOrWhiteSpace(approvedStateTableJson))
        {
            stateAfter = approvedStateTableJson.Trim();
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }
        else
        {
            await NotifyStepAsync(notifier, generationRunId, PipelineStep.PostState, finalizeProgress.ElapsedMs,
                "Finalize: deriving post-scene state from accepted prose.", cancellationToken);
            stateAfter = await GeneratePostStateAsync(db, ollama, writer, run, scene, draft, worldBlock, finalizeProgress, cancellationToken);
            await SaveSnapshotAsync(db, generationRunId, PipelineStep.PostState, stateAfter, cancellationToken);
        }

        var preSnap = await db.StateSnapshots.AsNoTracking()
            .Where(s => s.GenerationRunId == generationRunId && s.Step == PipelineStep.PreState)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var stateBefore = preSnap?.StateJson ?? "{}";

        await NotifyStepAsync(notifier, generationRunId, PipelineStep.TransitionCheck, finalizeProgress.ElapsedMs,
            "Finalize: continuity check across before / prose / after.", cancellationToken);
        await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, finalizeProgress, cancellationToken);

        run.FinalDraftText = draft;
        run.Status = GenerationRunStatus.Succeeded;
        run.CompletedAt = DateTimeOffset.UtcNow;
        scene.LatestDraftText = draft;
        scene.ApprovedStateTableJson = stateAfter;
        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyAsync(generationRunId, "RunFinished", "Succeeded",
            "Finalization complete; approved state saved to the scene.", cancellationToken, finalizeProgress.ElapsedMs(), null, null);
        return new FinalizeGenerationResult(stateAfter);
    }

    public async Task CorrectDraftAsync(Guid sceneId, Guid generationRunId, string userInstruction,
        CancellationToken cancellationToken = default)
    {
        var ins = userInstruction.Trim();
        if (string.IsNullOrEmpty(ins))
            throw new ArgumentException("Instruction is required.", nameof(userInstruction));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var writer = _ollamaOptions.Value.WriterModel;

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
        var worldBlock = WorldContextBuilder.Build(book, worldElements);
        var draft = run.FinalDraftText ?? scene.LatestDraftText ?? string.Empty;
        if (string.IsNullOrEmpty(draft))
            throw new InvalidOperationException("No draft to revise.");

        var text = await RepairDraftWithUserInstructionAsync(db, ollama, writer, run, draft, ins, worldBlock, cancellationToken);
        run.FinalDraftText = text;
        scene.LatestDraftText = text;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecutePipelineAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var writer = _ollamaOptions.Value.WriterModel;
        var critic = _ollamaOptions.Value.CriticModel;
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
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var scene = run.Scene;
            var book = scene.Chapter.Book;
            var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
            var worldBlock = WorldContextBuilder.Build(book, worldElements);
            var minWords = Math.Max(100, run.MinWordsOverride ?? _ollamaOptions.Value.DraftMinWords);

            await NotifyStepAsync(notifier, runId, PipelineStep.PreState, progress.ElapsedMs,
                "Pre-state: resolving beginning narrative state (author JSON, prior scene, or LLM).", cancellationToken);
            var stateBefore = await ResolveBeginningStateAsync(db, ollama, writer, run, scene, worldBlock, runId, progress, cancellationToken);
            await SaveSnapshotAsync(db, runId, PipelineStep.PreState, stateBefore, cancellationToken);

            await NotifyStepAsync(notifier, runId, PipelineStep.Draft, progress.ElapsedMs,
                "Draft: asking the writer model to produce the scene prose.", cancellationToken);
            var draft = await GenerateDraftAsync(db, ollama, writer, run, scene, stateBefore, worldBlock, minWords, progress, cancellationToken);

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
                        return await ChatAndLogAsync(db, ollama, writer, run.Id, PipelineStep.AgentEdit, system, user,
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
                var stateAfter = await GeneratePostStateAsync(db, ollama, writer, run, scene, draft, worldBlock, progress, cancellationToken);
                await SaveSnapshotAsync(db, runId, PipelineStep.PostState, stateAfter, cancellationToken);

                await NotifyStepAsync(notifier, runId, PipelineStep.TransitionCheck, progress.ElapsedMs,
                    "Transition check: verifying continuity before → prose → after.", cancellationToken);
                var transitionOk = await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, progress, cancellationToken);
                if (!transitionOk)
                    _logger.LogWarning("Transition check reported gaps for run {RunId}", runId);
            }

            var text = draft;
            var repairAttempt = 0;
            ComplianceVerdict? lastCompliance = null;

            while (repairAttempt < run.MaxRepairIterations)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.Compliance, progress.ElapsedMs,
                    "Compliance: checking the draft against scene instructions and world context.", cancellationToken);
                lastCompliance = await EvaluateComplianceAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, progress, cancellationToken);
                if (lastCompliance.Pass)
                    break;

                repairAttempt++;
                await NotifyStepAsync(notifier, runId, PipelineStep.Repair, progress.ElapsedMs,
                    $"Repair (compliance): revision pass {repairAttempt} of {run.MaxRepairIterations}.", cancellationToken);
                await notifier.NotifyAsync(runId, "RepairAttempt", PipelineStep.Repair.ToString(),
                    $"Addressing compliance issues — attempt {repairAttempt}.", cancellationToken,
                    progress.ElapsedMs(), null, null);
                text = await RepairDraftForComplianceAsync(db, ollama, writer, run, text, lastCompliance, worldBlock, minWords, progress, cancellationToken);
                if (!run.StopAfterDraft)
                {
                    var newAfter = await GeneratePostStateAsync(db, ollama, writer, run, scene, text, worldBlock, progress, cancellationToken);
                    await SaveSnapshotAsync(db, runId, PipelineStep.PostState, newAfter, cancellationToken);
                }
            }

            if (lastCompliance is not { Pass: true })
                throw new InvalidOperationException("Instruction compliance did not pass after maximum repair attempts.");

            repairAttempt = 0;
            QualityVerdict? lastQuality = null;
            while (repairAttempt < run.MaxRepairIterations)
            {
                await NotifyStepAsync(notifier, runId, PipelineStep.Quality, progress.ElapsedMs,
                    "Quality: prose critique pass (metaphor, voice, on-the-nose labels).", cancellationToken);
                lastQuality = await EvaluateQualityAsync(db, ollama, critic, run, scene, text, worldBlock, progress, cancellationToken);
                if (lastQuality.Pass)
                    break;

                repairAttempt++;
                await NotifyStepAsync(notifier, runId, PipelineStep.Repair, progress.ElapsedMs,
                    $"Repair (quality): revision pass {repairAttempt} of {run.MaxRepairIterations}.", cancellationToken);
                await notifier.NotifyAsync(runId, "RepairAttempt", PipelineStep.Repair.ToString(),
                    $"Addressing quality feedback — attempt {repairAttempt}.", cancellationToken,
                    progress.ElapsedMs(), null, null);
                text = await RepairDraftForQualityAsync(db, ollama, writer, run, text, lastQuality, worldBlock, minWords, progress, cancellationToken);
            }

            if (lastQuality is not { Pass: true })
                throw new InvalidOperationException("Prose quality gate did not pass after maximum repair attempts.");

            run.FinalDraftText = text;
            scene.LatestDraftText = text;
            await db.SaveChangesAsync(cancellationToken);

            if (run.StopAfterDraft)
            {
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
        string model,
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
            "No author or prior-scene state — asking the writer model to infer pre-scene JSON.", cancellationToken,
            progress.ElapsedMs(), null, null);
        return await GeneratePreStateAsync(db, ollama, model, run, scene, sameScenePrior, worldBlock, progress, cancellationToken);
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
        var system = """
            You output ONLY valid JSON matching a narrative state snapshot. No markdown fences.
            Schema: { "schemaVersion": 1, "transitionSummary": string|null, "characters": [...], "spatial": {...}, "dialogue": {...}, "knowledge": {...}, "environment": {...}, "plotDevices": string[] }.
            Characters entries: id, name, location, pose, clothing, emotionalState, traitsShownNotTold (short notes for showing traits through action, not labels).
            Infer reasonable detail from the scene brief; keep consistent with prior state if provided.
            Honor world-building and story tone when inferring locations, culture, and facts.
            """;
        var user = $"""
            Scene title: {scene.Title}
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Narrative perspective (follow strictly): {scene.NarrativePerspective ?? "(infer from story tone if not specified)"}
            Narrative tense (follow strictly): {scene.NarrativeTense ?? "(infer from story tone if not specified)"}
            Prior state JSON (may be empty): {priorStateJson ?? "{}"}

            {worldContextBlock}

            Produce state BEFORE the prose is written (establishing shot).
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PreState, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
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
        PipelineProgress progress,
        CancellationToken cancellationToken)
    {
        var numPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict);
        var proseOptions = new OllamaChatOptions { NumPredict = numPredict };
        var targetBand = Math.Min(2000, Math.Max(minWords, 1500));

        var system = """
            You are a fiction writer producing long-form prose for novels and serial fiction.
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

            Write the complete scene. Target roughly {minWords}–{targetBand} words for this session unless the brief explicitly demands a shorter piece.
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Draft, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Write scene draft (prose)");
        text = text.Trim();

        if (_ollamaOptions.Value.DraftExpandIfShort && CountWords(text) < minWords)
        {
            _logger.LogInformation(
                "Draft short ({Words} words, min {Min}); running expansion pass for run {RunId}",
                CountWords(text), minWords, run.Id);
            var expandSystem = """
                You expand fiction for long-form publication. Continue in the same voice, tense, and POV.
                Add substantive prose—new paragraphs, beats, dialogue, sensory detail—not repetition of the same lines.
                Do not summarize the scene; extend it. Output prose only, no preamble.
                Do not introduce new characters or world elements that are not already in the draft or listed under Linked world-building
                or the scene instructions in the user message.
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
            var (expanded, _) = await ChatAndLogAsync(
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
        string draftText,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system = """
            From the prose only (and the scene title for disambiguation), derive the narrative state AFTER the scene.
            Output ONLY valid JSON, same schema as before (schemaVersion, characters, spatial, dialogue, knowledge, environment, plotDevices, transitionSummary).
            No markdown fences.
            World-building below is context only; derive the state from the prose.
            """;
        var user = $"""
            Scene title: {scene.Title}
            Prose:
            {draftText}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PostState, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
            "Derive post-scene narrative state (JSON)");
        return LlmJson.StripMarkdownFences(text);
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
        var system = """
            You verify narrative continuity. Output ONLY JSON: { "pass": bool, "gaps": string[] }.
            Check that the prose plausibly transitions from stateBefore to stateAfter; list concrete gaps if not.
            Flag contradictions with established world-building or story tone when relevant.
            """;
        var user = $"""
            stateBefore: {stateBefore}
            prose: {draft}
            stateAfter: {stateAfter}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.TransitionCheck, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
            "Continuity check (before / prose / after)");
        var verdict = LlmJson.Deserialize<TransitionVerdict>(text);
        var pass = verdict?.Pass ?? false;
        await db.ComplianceEvaluations.AddAsync(new ComplianceEvaluation
        {
            Id = Guid.NewGuid(),
            GenerationRunId = run.Id,
            Passed = pass,
            Kind = "Transition",
            AttemptNumber = 0,
            VerdictJson = JsonSerializer.Serialize(verdict ?? new TransitionVerdict { Pass = false, Gaps = new List<string> { "parse_failed" } }),
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
        var system = """
            You check instruction compliance. Output ONLY JSON:
            { "pass": bool, "violations": string[], "fixInstructions": string[] }.
            Violations: wrong ending vs instructions, invented characters or facts, ignored constraints, contradictions of linked world-building or story tone.
            fixInstructions: minimal edits to fix issues while preserving voice.
            """;
        var user = $"""
            Scene synopsis and instructions:
            {SceneInstructionsForAgent(scene)}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}
            stateBefore: {stateBefore}
            draft: {draft}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Compliance, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
            "Instruction compliance check");
        var verdict = LlmJson.Deserialize<ComplianceVerdict>(text)
                      ?? new ComplianceVerdict { Pass = true, Violations = new List<string>(), FixInstructions = new List<string>() };
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
        string draft,
        string worldContextBlock,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var system = """
            You critique prose quality for long-form fiction. Output ONLY JSON:
            { "pass": bool, "issues": string[], "fixInstructions": string[] }.
            Flag ambiguous or incongruous metaphors/similes, on-the-nose trait labels (e.g. "being analytical"), over-explanation of motives, flat characters.
            Ensure tone aligns with the story-level description when provided.
            fixInstructions: targeted rewrites; preserve plot and compliance.
            """;
        var user = $"""
            Scene synopsis and instructions (for tone): {SceneInstructionsForAgent(scene)}
            Narrative perspective: {scene.NarrativePerspective ?? "(any)"}
            Narrative tense: {scene.NarrativeTense ?? "(any)"}
            draft: {draft}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Quality, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken, progress,
            "Prose quality critique");
        var verdict = LlmJson.Deserialize<QualityVerdict>(text)
                      ?? new QualityVerdict { Pass = true, Issues = new List<string>(), FixInstructions = new List<string>() };
        await db.ComplianceEvaluations.AddAsync(new ComplianceEvaluation
        {
            Id = Guid.NewGuid(),
            GenerationRunId = run.Id,
            Passed = verdict.Pass,
            Kind = "Quality",
            AttemptNumber = 0,
            VerdictJson = JsonSerializer.Serialize(verdict),
            CreatedAt = DateTimeOffset.UtcNow
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return verdict;
    }

    private async Task<string> RepairDraftForComplianceAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        string draft,
        ComplianceVerdict verdict,
        string worldContextBlock,
        int minWords,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var system = """
            Revise the draft to address the issues. Preserve voice and plot. Output prose only.
            Do not contradict story tone or linked world-building.
            Keep the result as substantial long-form scene prose (multiple paragraphs when appropriate), not a brief summary.
            """;
        var fixList = string.Join("\n", verdict.FixInstructions);
        var user = $"""
            Current draft:
            {draft}

            Violations to fix:
            {string.Join("\n", verdict.Violations)}

            Fix instructions:
            {fixList}

            {worldContextBlock}

            The revised scene should be at least {minWords} words unless the scene instructions explicitly require brevity.
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Repair draft for compliance");
        return text.Trim();
    }

    private async Task<string> RepairDraftForQualityAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        string draft,
        QualityVerdict verdict,
        string worldContextBlock,
        int minWords,
        PipelineProgress? progress,
        CancellationToken cancellationToken)
    {
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var system = """
            Revise the draft to address the prose issues. Preserve plot and instruction compliance. Output prose only.
            Maintain substantial scene length (multiple paragraphs) suitable for long-form fiction, not a terse fix.
            """;
        var user = $"""
            Current draft:
            {draft}

            Issues:
            {string.Join("\n", verdict.Issues)}

            Fix instructions:
            {string.Join("\n", verdict.FixInstructions)}

            {worldContextBlock}

            The revised scene should be at least {minWords} words unless the scene instructions explicitly require brevity.
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken, progress,
            "Repair draft for quality");
        return text.Trim();
    }

    private async Task<string> RepairDraftWithUserInstructionAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        string draft,
        string userInstruction,
        string worldContextBlock,
        CancellationToken cancellationToken)
    {
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var system = """
            You revise fiction prose according to the author's explicit instructions. Preserve continuity and voice unless the author asks otherwise.
            Output prose only, no preamble.
            """;
        var user = $"""
            Author instruction:
            {userInstruction}

            Current draft:
            {draft}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken);
        return text.Trim();
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

    private async Task<(string messageText, string rawResponse)> ChatAndLogAsync(
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
        await db.LlmCalls.AddAsync(new LlmCall
        {
            Id = Guid.NewGuid(),
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
            var preview = TruncatePreview(result.MessageText, 520);
            await progress.Notifier.NotifyAsync(runId, "LlmRoundtrip", step.ToString(),
                $"{label}: model «{model}» returned {result.MessageText.Length:N0} characters in {roundSw.ElapsedMilliseconds} ms.",
                cancellationToken,
                progress.ElapsedMs(),
                roundSw.ElapsedMilliseconds,
                preview,
                req);
        }

        return (result.MessageText, result.MessageText);
    }

    private static string TruncatePreview(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var t = text.Trim();
        if (t.Length <= maxChars)
            return t;
        return t[..maxChars] + "…";
    }
}
