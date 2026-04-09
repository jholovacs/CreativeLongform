using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace CreativeLongform.Api.Tests;

/// <summary>PostgreSQL (Testcontainers) + <see cref="WebApplicationFactory{TEntryPoint}"/>. Requires Docker.</summary>
public sealed class CreativeLongformApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("creativelongform")
            .WithUsername("creative")
            .WithPassword("creative")
            .Build();
        await _postgres.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_postgres is null)
            throw new InvalidOperationException("PostgreSQL container was not started.");

        // UseSetting wins over appsettings.json for integration tests
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("DisableHttpsRedirection", "true");
        builder.UseSetting("Ollama:BaseUrl", "http://127.0.0.1:11434/api");
        builder.UseSetting("Ollama:WriterModel", "llama3.2");
        builder.UseSetting("Ollama:CriticModel", "llama3.2");
        builder.UseSetting("Ollama:AgenticEditEnabled", "false");
        builder.UseSetting("Ollama:AgenticEditMaxTurns", "0");
        builder.UseEnvironment("Development");
    }
}
