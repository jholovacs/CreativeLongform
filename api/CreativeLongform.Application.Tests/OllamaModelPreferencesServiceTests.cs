using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using CreativeLongform.Application.Services;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using CreativeLongform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Application.Tests;

public class OllamaModelPreferencesServiceTests
{
    private static (CreativeLongformDbContext Db, OllamaModelPreferencesService Service) CreateService(
        Action<OllamaOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CreativeLongformDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddScoped<ICreativeLongformDbContext>(sp => sp.GetRequiredService<CreativeLongformDbContext>());
        services.Configure<OllamaOptions>(o =>
        {
            o.WriterModel = "writer-default";
            o.CriticModel = "critic-default";
            o.AgentModel = null;
            o.WorldBuildingModel = null;
            o.PreStateModel = null;
            o.PostStateModel = null;
            configure?.Invoke(o);
        });
        services.AddScoped<OllamaModelPreferencesService>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<CreativeLongformDbContext>(),
            provider.GetRequiredService<OllamaModelPreferencesService>());
    }

    [Fact]
    public async Task GetWriterModelAsync_uses_db_override_when_set()
    {
        var (db, svc) = CreateService();
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            WriterModel = "writer-db",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var model = await svc.GetWriterModelAsync();

        Assert.Equal("writer-db", model);
    }

    [Fact]
    public async Task GetPreStateModelAsync_cascades_to_writer_when_unset()
    {
        var (db, svc) = CreateService();
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            WriterModel = "writer-db",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var model = await svc.GetPreStateModelAsync();

        Assert.Equal("writer-db", model);
    }

    [Fact]
    public async Task GetEditorModelAsync_cascades_to_writer_when_unset()
    {
        var (db, svc) = CreateService();
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            WriterModel = "writer-db",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var model = await svc.GetEditorModelAsync();

        Assert.Equal("writer-db", model);
    }

    [Fact]
    public async Task UpdateAssignmentsAsync_writes_change_log_for_role()
    {
        var (db, svc) = CreateService();
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            WriterModel = "old-writer",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await svc.UpdateAssignmentsAsync(new OllamaModelAssignmentsPatch { WriterModel = "new-writer" }, "test");

        var log = await db.OllamaModelChangeLogs.AsNoTracking()
            .SingleAsync(x => x.Role == OllamaModelRole.Writer);
        Assert.Equal(OllamaModelRole.Writer, log.Role);
        Assert.Equal("old-writer", log.PreviousModel);
        Assert.Equal("new-writer", log.NewModel);
    }

    [Fact]
    public async Task UpdateAssignmentsAsync_clearWriter_restores_appsettings_default()
    {
        var (db, svc) = CreateService();
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            WriterModel = "writer-db",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await svc.UpdateAssignmentsAsync(new OllamaModelAssignmentsPatch { ClearWriter = true }, "test");

        var assignments = await svc.GetAssignmentsAsync();
        Assert.Equal("writer-default", assignments.WriterModel);
    }

    [Fact]
    public async Task GetConnectionSettingsAsync_uses_db_override_when_set()
    {
        var (db, svc) = CreateService(o => o.BaseUrl = "http://localhost:11434/api");
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            BaseUrl = "http://192.168.1.50:11434",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var settings = await svc.GetConnectionSettingsAsync();

        Assert.True(settings.IsDbOverridden);
        Assert.EndsWith("/api", settings.EffectiveBaseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("192.168.1.50", settings.EffectiveBaseUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAssignmentsAsync_normalizes_base_url_and_logs_connection_change()
    {
        var (db, svc) = CreateService(o => o.BaseUrl = "http://localhost:11434/api");

        await svc.UpdateAssignmentsAsync(new OllamaModelAssignmentsPatch
        {
            BaseUrl = "http://dev-ai.local:11434"
        }, "test");

        var settings = await svc.GetConnectionSettingsAsync();
        Assert.EndsWith("/api", settings.EffectiveBaseUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dev-ai.local", settings.EffectiveBaseUrl, StringComparison.Ordinal);

        var log = await db.OllamaModelChangeLogs.AsNoTracking()
            .SingleAsync(x => x.Role == OllamaModelRole.Connection);
        Assert.Equal(OllamaModelRole.Connection, log.Role);
    }

    [Fact]
    public async Task UpdateAssignmentsAsync_clearBaseUrl_restores_configuration_default()
    {
        var (db, svc) = CreateService(o => o.BaseUrl = "http://localhost:11434/api");
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            BaseUrl = "http://remote:11434/api",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        await svc.UpdateAssignmentsAsync(new OllamaModelAssignmentsPatch { ClearBaseUrl = true }, "test");

        var settings = await svc.GetConnectionSettingsAsync();
        Assert.False(settings.IsDbOverridden);
        Assert.Contains("localhost", settings.EffectiveBaseUrl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetAgentModelAsync_uses_appsettings_when_db_and_cascade_unset()
    {
        var (db, svc) = CreateService(o =>
        {
            o.WriterModel = "writer-default";
            o.AgentModel = "agent-from-config";
        });
        db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var model = await svc.GetAgentModelAsync();

        Assert.Equal("agent-from-config", model);
    }
}
