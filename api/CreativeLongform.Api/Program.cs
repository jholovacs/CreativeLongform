using CreativeLongform.Api.Hubs;
using CreativeLongform.Api.OData;
using CreativeLongform.Api.Services;
using CreativeLongform.Application.Abstractions;
using CreativeLongform.Infrastructure;
using CreativeLongform.Infrastructure.Data;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200",
                "http://127.0.0.1:4200",
                "http://localhost:8080",
                "http://127.0.0.1:8080")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSingleton<IGenerationProgressNotifier, SignalRGenerationProgressNotifier>();
builder.Services.AddHttpClient();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSignalR(options =>
{
    // Progress events carry full agent-edit JSON (up to ~500k chars) plus large request payloads.
    options.MaximumReceiveMessageSize = 8 * 1024 * 1024;
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    })
    .AddOData(options =>
    {
        options.Filter().OrderBy().Expand().Select().SetMaxTop(1000).Count();
        options.AddRouteComponents("odata", ODataEdmModelBuilder.Build());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
if (!app.Configuration.GetValue("DisableHttpsRedirection", false))
    app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHub<GenerationHub>("/hubs/generation");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CreativeLongformDbContext>();
    await db.Database.MigrateAsync();
    if (app.Environment.IsDevelopment())
        await DbSeeder.SeedDevelopmentAsync(db);
}

app.Run();

/// <summary>Exposes Program to integration tests (WebApplicationFactory).</summary>
public partial class Program;
