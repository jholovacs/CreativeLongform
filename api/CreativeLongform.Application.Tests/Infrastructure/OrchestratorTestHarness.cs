using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using CreativeLongform.Application.Services;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using CreativeLongform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CreativeLongform.Application.Tests.Infrastructure;

/// <summary>In-memory DB + stub Ollama for application service unit tests.</summary>
public sealed class OrchestratorTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _root;
    private readonly AsyncServiceScope _scope;

    public FakeOllamaClient Ollama { get; }
    public CreativeLongformDbContext Db { get; }
    public IGenerationOrchestrator Orchestrator { get; }
    public IWorldBuildingService WorldBuilding { get; }
    public IDraftRecommendationService DraftRecommendations { get; }

    private OrchestratorTestHarness(ServiceProvider root, AsyncServiceScope scope, FakeOllamaClient ollama)
    {
        _root = root;
        _scope = scope;
        Ollama = ollama;
        Db = scope.ServiceProvider.GetRequiredService<CreativeLongformDbContext>();
        Orchestrator = scope.ServiceProvider.GetRequiredService<IGenerationOrchestrator>();
        WorldBuilding = scope.ServiceProvider.GetRequiredService<IWorldBuildingService>();
        DraftRecommendations = scope.ServiceProvider.GetRequiredService<IDraftRecommendationService>();
    }

    public static OrchestratorTestHarness Create(Action<OllamaOptions>? configure = null)
    {
        var ollama = new FakeOllamaClient();
        var dbName = Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<CreativeLongformDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<ICreativeLongformDbContext>(sp => sp.GetRequiredService<CreativeLongformDbContext>());
        services.Configure<OllamaOptions>(o =>
        {
            o.WriterModel = "test-writer";
            o.CriticModel = "test-critic";
            o.PreStateModel = "test-pre";
            o.PostStateModel = "test-post";
            o.WorldBuildingModel = "test-world";
            o.AgenticEditEnabled = false;
            o.QualityGateEnabled = false;
            o.DraftExpandIfShort = false;
            o.DraftMinWords = 100;
            configure?.Invoke(o);
        });
        services.AddSingleton<IOllamaClient>(ollama);
        services.AddScoped<IOllamaModelPreferencesService, OllamaModelPreferencesService>();
        services.AddSingleton<IGenerationRunCancellationRegistry, GenerationRunCancellationRegistry>();
        services.AddSingleton<IGenerationProgressNotifier, NoOpGenerationProgressNotifier>();
        services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();
        services.AddScoped<IWorldBuildingService, WorldBuildingService>();
        services.AddScoped<IDraftRecommendationService, DraftRecommendationService>();

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        return new OrchestratorTestHarness(root, scope, ollama);
    }

    public async Task<GenerationRunStatus> GetRunStatusAsync(Guid runId)
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CreativeLongformDbContext>();
        return (await db.GenerationRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).Status;
    }

    public async Task<GenerationRun> WaitForRunAsync(Guid runId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = _root.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CreativeLongformDbContext>();
            var run = await db.GenerationRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
            if (run.Status is not (GenerationRunStatus.Pending or GenerationRunStatus.Running))
                return run;
            await Task.Delay(25);
        }

        throw new TimeoutException($"Generation run {runId} did not finish within {timeout?.TotalSeconds ?? 15}s.");
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _root.DisposeAsync();
    }
}
