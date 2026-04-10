using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using CreativeLongform.Application.Services;
using CreativeLongform.Infrastructure.Data;
using CreativeLongform.Infrastructure.Ollama;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CreativeLongform.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' not found.");

        services.AddDbContext<CreativeLongformDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<ICreativeLongformDbContext>(sp =>
            sp.GetRequiredService<CreativeLongformDbContext>());

        services.Configure<OllamaOptions>(configuration.GetSection(OllamaOptions.SectionName));

        services.AddScoped<IOllamaModelPreferencesService, OllamaModelPreferencesService>();

        services.AddHttpClient<IOllamaAdminApi, OllamaAdminApi>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IConfiguration>().GetSection(OllamaOptions.SectionName)
                .Get<OllamaOptions>() ?? new OllamaOptions();
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(60);
        });

        services.AddHttpClient<IOllamaClient, OllamaClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IConfiguration>().GetSection(OllamaOptions.SectionName)
                .Get<OllamaOptions>() ?? new OllamaOptions();
            client.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(15);
        });

        services.AddSingleton<IGenerationRunCancellationRegistry, GenerationRunCancellationRegistry>();
        services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();
        services.AddScoped<IWorldBuildingService, WorldBuildingService>();
        services.AddScoped<IDraftRecommendationService, DraftRecommendationService>();

        return services;
    }
}
