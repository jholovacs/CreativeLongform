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
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenerationOrchestrator> _logger;
    private readonly IOptions<OllamaOptions> _ollamaOptions;

    public GenerationOrchestrator(
        IServiceScopeFactory scopeFactory,
        ILogger<GenerationOrchestrator> logger,
        IOptions<OllamaOptions> ollamaOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _ollamaOptions = ollamaOptions;
    }

    public async Task<Guid> StartGenerationAsync(Guid sceneId, string? idempotencyKey, CancellationToken cancellationToken = default)
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
            MaxRepairIterations = 5
        };
        db.GenerationRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var runId = run.Id;
        _ = Task.Run(() => ExecutePipelineAsync(runId, CancellationToken.None), CancellationToken.None);

        await notifier.NotifyAsync(runId, "RunStarted", nameof(PipelineStep.PreState), null, cancellationToken);
        return runId;
    }

    private async Task ExecutePipelineAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ICreativeLongformDbContext>();
        var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
        var notifier = scope.ServiceProvider.GetRequiredService<IGenerationProgressNotifier>();
        var writer = _ollamaOptions.Value.WriterModel;
        var critic = _ollamaOptions.Value.CriticModel;

        var run = await db.GenerationRuns
            .Include(r => r.Scene)
                .ThenInclude(s => s.Chapter)
                    .ThenInclude(c => c.Book)
            .Include(r => r.Scene)
                .ThenInclude(s => s.SceneWorldElements)
                .ThenInclude(swe => swe.WorldElement)
            .FirstAsync(r => r.Id == runId, cancellationToken);

        run.MaxRepairIterations = Math.Max(1, run.MaxRepairIterations);
        run.Status = GenerationRunStatus.Running;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var scene = run.Scene;
            var book = scene.Chapter.Book;
            var worldElements = scene.SceneWorldElements.Select(swe => swe.WorldElement).ToList();
            var worldBlock = WorldContextBuilder.Build(book, worldElements);
            var priorStateJson = await GetLatestSucceededPostStateJsonAsync(db, scene.Id, runId, cancellationToken);

            await StepAsync(notifier, runId, PipelineStep.PreState, cancellationToken);
            var stateBefore = await GeneratePreStateAsync(db, ollama, writer, run, scene, priorStateJson, worldBlock, cancellationToken);
            await SaveSnapshotAsync(db, runId, PipelineStep.PreState, stateBefore, cancellationToken);

            await StepAsync(notifier, runId, PipelineStep.Draft, cancellationToken);
            var draft = await GenerateDraftAsync(db, ollama, writer, run, scene, stateBefore, worldBlock, cancellationToken);

            if (_ollamaOptions.Value.AgenticEditEnabled && _ollamaOptions.Value.AgenticEditMaxTurns > 0)
            {
                await StepAsync(notifier, runId, PipelineStep.AgentEdit, cancellationToken);
                var agentTurns = Math.Max(1, _ollamaOptions.Value.AgenticEditMaxTurns);
                var agentPredict = Math.Max(512, _ollamaOptions.Value.AgenticEditNumPredict);
                draft = await AgenticEditLoop.RunAsync(
                    draft,
                    scene.Instructions,
                    scene.ExpectedEndStateNotes,
                    worldBlock,
                    agentTurns,
                    _logger,
                    async (system, user, ct) =>
                    {
                        var o = new OllamaChatOptions { NumPredict = agentPredict };
                        return await ChatAndLogAsync(db, ollama, writer, run.Id, PipelineStep.AgentEdit, system, user,
                            jsonFormat: true, o, ct);
                    },
                    notifier,
                    runId,
                    cancellationToken);
            }

            await StepAsync(notifier, runId, PipelineStep.PostState, cancellationToken);
            var stateAfter = await GeneratePostStateAsync(db, ollama, writer, run, scene, draft, worldBlock, cancellationToken);
            await SaveSnapshotAsync(db, runId, PipelineStep.PostState, stateAfter, cancellationToken);

            await StepAsync(notifier, runId, PipelineStep.TransitionCheck, cancellationToken);
            var transitionOk = await RunTransitionCheckAsync(db, ollama, critic, run, stateBefore, draft, stateAfter, worldBlock, cancellationToken);
            if (!transitionOk)
                _logger.LogWarning("Transition check reported gaps for run {RunId}", runId);

            var text = draft;
            var repairAttempt = 0;
            ComplianceVerdict? lastCompliance = null;

            while (repairAttempt < run.MaxRepairIterations)
            {
                await StepAsync(notifier, runId, PipelineStep.Compliance, cancellationToken);
                lastCompliance = await EvaluateComplianceAsync(db, ollama, critic, run, scene, stateBefore, text, worldBlock, cancellationToken);
                if (lastCompliance.Pass)
                    break;

                repairAttempt++;
                await StepAsync(notifier, runId, PipelineStep.Repair, cancellationToken);
                await notifier.NotifyAsync(runId, "RepairAttempt", PipelineStep.Repair.ToString(),
                    $"compliance:{repairAttempt}", cancellationToken);
                text = await RepairDraftForComplianceAsync(db, ollama, writer, run, text, lastCompliance, worldBlock, cancellationToken);
                var newAfter = await GeneratePostStateAsync(db, ollama, writer, run, scene, text, worldBlock, cancellationToken);
                await SaveSnapshotAsync(db, runId, PipelineStep.PostState, newAfter, cancellationToken);
            }

            if (lastCompliance is not { Pass: true })
                throw new InvalidOperationException("Instruction compliance did not pass after maximum repair attempts.");

            repairAttempt = 0;
            QualityVerdict? lastQuality = null;
            while (repairAttempt < run.MaxRepairIterations)
            {
                await StepAsync(notifier, runId, PipelineStep.Quality, cancellationToken);
                lastQuality = await EvaluateQualityAsync(db, ollama, critic, run, scene, text, worldBlock, cancellationToken);
                if (lastQuality.Pass)
                    break;

                repairAttempt++;
                await StepAsync(notifier, runId, PipelineStep.Repair, cancellationToken);
                await notifier.NotifyAsync(runId, "RepairAttempt", PipelineStep.Repair.ToString(),
                    $"quality:{repairAttempt}", cancellationToken);
                text = await RepairDraftForQualityAsync(db, ollama, writer, run, text, lastQuality, worldBlock, cancellationToken);
            }

            if (lastQuality is not { Pass: true })
                throw new InvalidOperationException("Prose quality gate did not pass after maximum repair attempts.");

            run.FinalDraftText = text;
            run.Status = GenerationRunStatus.Succeeded;
            run.CompletedAt = DateTimeOffset.UtcNow;
            scene.LatestDraftText = text;
            await db.SaveChangesAsync(cancellationToken);
            await notifier.NotifyAsync(runId, "RunFinished", "Succeeded", null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generation failed for run {RunId}", runId);
            run = await db.GenerationRuns.FirstAsync(r => r.Id == runId, cancellationToken);
            run.Status = GenerationRunStatus.Failed;
            run.FailureReason = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            await notifier.NotifyAsync(runId, "RunFinished", "Failed", ex.Message, cancellationToken);
        }
    }

    private static async Task StepAsync(
        IGenerationProgressNotifier notifier,
        Guid runId,
        PipelineStep step,
        CancellationToken cancellationToken)
    {
        await notifier.NotifyAsync(runId, "StepStarted", step.ToString(), null, cancellationToken);
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

    private async Task<string> GeneratePreStateAsync(
        ICreativeLongformDbContext db,
        IOllamaClient ollama,
        string model,
        GenerationRun run,
        Scene scene,
        string? priorStateJson,
        string worldContextBlock,
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
            Instructions: {scene.Instructions}
            Prior state JSON (may be empty): {priorStateJson ?? "{}"}

            {worldContextBlock}

            Produce state BEFORE the prose is written (establishing shot).
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PreState, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var minWords = Math.Max(100, _ollamaOptions.Value.DraftMinWords);
        var numPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict);
        var proseOptions = new OllamaChatOptions { NumPredict = numPredict };

        var system = """
            You are a fiction writer producing long-form prose for novels and serial fiction.
            Follow the scene instructions and the established narrative state.
            Write vivid prose; avoid naming character traits explicitly when a bio already labels them—show through action and detail.
            Respect story tone and linked world-building; do not invent facts that contradict them.
            Develop the scene with multiple paragraphs: setting, action, dialogue, and character interiority as fits the brief.
            Do not stop after a few sentences; this is a full scene beat, not a summary.
            Output prose only, no preamble or title line.
            """;
        var user = $"""
            State before (JSON): {stateBeforeJson}
            Scene instructions: {scene.Instructions}
            Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

            {worldContextBlock}

            Write the complete scene. Target at least {minWords} words unless the instructions explicitly demand a shorter piece.
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Draft, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken);
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
                """;
            var expandUser = $"""
                The draft below is too short for this novel scene. It must reach at least {minWords} words total.
                Scene instructions: {scene.Instructions}
                Expected end notes (if any): {scene.ExpectedEndStateNotes ?? "(none)"}

                {worldContextBlock}

                Expand and continue from the end of the draft below (you may revise transitions so it reads as one scene):
                {text}
                """;
            var (expanded, _) = await ChatAndLogAsync(
                db, ollama, model, run.Id, PipelineStep.Draft, expandSystem, expandUser, jsonFormat: false, proseOptions, cancellationToken: cancellationToken);
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
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.PostState, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken);
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
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.TransitionCheck, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var system = """
            You check instruction compliance. Output ONLY JSON:
            { "pass": bool, "violations": string[], "fixInstructions": string[] }.
            Violations: wrong ending vs instructions, invented characters or facts, ignored constraints, contradictions of linked world-building or story tone.
            fixInstructions: minimal edits to fix issues while preserving voice.
            """;
        var user = $"""
            Instructions: {scene.Instructions}
            Expected end notes: {scene.ExpectedEndStateNotes ?? "(none)"}
            stateBefore: {stateBefore}
            draft: {draft}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Compliance, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken);
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
            Scene instructions (for tone): {scene.Instructions}
            draft: {draft}

            {worldContextBlock}
            """;
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Quality, system, user, jsonFormat: true, chatOptions: null, cancellationToken: cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var minWords = Math.Max(100, _ollamaOptions.Value.DraftMinWords);
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
        var (text, _) = await ChatAndLogAsync(db, ollama, model, run.Id, PipelineStep.Repair, system, user, jsonFormat: false, proseOptions, cancellationToken: cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var proseOptions = new OllamaChatOptions { NumPredict = Math.Max(1024, _ollamaOptions.Value.DraftNumPredict) };
        var minWords = Math.Max(100, _ollamaOptions.Value.DraftMinWords);
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
        CancellationToken cancellationToken = default)
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
        var result = await ollama.ChatAsync(model, messages, jsonFormat, chatOptions, cancellationToken);
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
        return (result.MessageText, result.MessageText);
    }
}
