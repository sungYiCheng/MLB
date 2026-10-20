using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using MlbAi.Application.Interfaces;
using MlbAi.Application.Models;
using MlbAi.Application.Services;
using MlbAi.Infrastructure.Services;

const string appName = "MLB AI Daily API";
const string mlbStatsApiBaseUrl = "https://statsapi.mlb.com/api/v1/";

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];
var appInsightsConnectionString =
    builder.Configuration["ApplicationInsights:ConnectionString"] ??
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
var isApplicationInsightsConfigured = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
var azureOpenAiOptions = new AzureOpenAiOptions(
    Endpoint: builder.Configuration["AzureOpenAI:Endpoint"] ?? string.Empty,
    DeploymentName: builder.Configuration["AzureOpenAI:DeploymentName"] ?? string.Empty,
    ApiVersion: builder.Configuration["AzureOpenAI:ApiVersion"] ?? "2024-10-21",
    ApiKey: builder.Configuration["AzureOpenAI:ApiKey"] ?? string.Empty);
var deploymentInfo = new DeploymentInfo(
    Version: builder.Configuration["AppVersion"] ?? "local",
    BuildId: builder.Configuration["AppBuildId"] ?? "local",
    ImageTag: builder.Configuration["AppImageTag"] ?? "local");

if (isApplicationInsightsConfigured)
{
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = appInsightsConnectionString;
    });
}

builder.Services
    .AddHttpClient<IMlbService, MlbStatsService>(client =>
    {
        client.BaseAddress = new Uri(mlbStatsApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddSingleton(azureOpenAiOptions);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<AzureOpenAiDailyBriefingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod();

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }
        else
        {
            policy.SetIsOriginAllowed(_ => false);
        }
    });
});

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Ok(new { name = appName, status = "ready" }));

app.MapGet("/health", (IHostEnvironment environment) =>
{
    var checks = new[]
    {
        new HealthCheckItem(
            Name: "api-process",
            Status: "healthy",
            Description: "The API process is running and can respond to HTTP requests."),
        new HealthCheckItem(
            Name: "mlb-stats-api-client",
            Status: "configured",
            Description: "The MLB Stats API HTTP client has a base URL configured; this check does not call the external MLB API."),
        new HealthCheckItem(
            Name: "cors-allowed-origins",
            Status: allowedOrigins.Length > 0 ? "configured" : "not-configured",
            Description: $"{allowedOrigins.Length} allowed origin(s) configured."),
        new HealthCheckItem(
            Name: "application-insights",
            Status: isApplicationInsightsConfigured ? "configured" : "not-configured",
            Description: isApplicationInsightsConfigured
                ? "Application Insights telemetry is configured for this process."
                : "Application Insights telemetry is not configured for this process."),
        new HealthCheckItem(
            Name: "azure-openai",
            Status: azureOpenAiOptions.IsConfigured ? "configured" : "preview-fallback",
            Description: azureOpenAiOptions.IsConfigured
                ? $"Azure OpenAI deployment '{azureOpenAiOptions.DeploymentName}' is configured."
                : "Azure OpenAI is not configured; Daily Briefing uses deterministic preview mode.")
    };

    return Results.Ok(new HealthResponse(
        Service: appName,
        Status: "healthy",
        Environment: environment.EnvironmentName,
        Deployment: deploymentInfo,
        TimestampUtc: DateTimeOffset.UtcNow,
        Checks: checks));
});

app.MapGet("/api/games/today", async (IMlbService mlbService, CancellationToken cancellationToken) =>
{
    try
    {
        var games = await mlbService.GetTodayGamesAsync(cancellationToken);
        return Results.Ok(games);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "MLB Stats API unavailable",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "MLB Stats API timeout",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/api/standings", async (IMlbService mlbService, CancellationToken cancellationToken) =>
{
    try
    {
        var standings = await mlbService.GetStandingsAsync(cancellationToken);
        return Results.Ok(standings);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "MLB Stats API unavailable",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "MLB Stats API timeout",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/api/stat-leaders", async (IMlbService mlbService, CancellationToken cancellationToken) =>
{
    try
    {
        var leaders = await mlbService.GetStatLeadersAsync(cancellationToken);
        return Results.Ok(leaders);
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "MLB Stats API unavailable",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "MLB Stats API timeout",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapGet("/api/ai/daily-briefing", async (
    IMlbService mlbService,
    AzureOpenAiDailyBriefingService azureOpenAiService,
    IMemoryCache cache,
    ILogger<AzureOpenAiDailyBriefingService> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var gamesTask = mlbService.GetTodayGamesAsync(cancellationToken);
        var standingsTask = mlbService.GetStandingsAsync(cancellationToken);
        var statLeadersTask = mlbService.GetStatLeadersAsync(cancellationToken);

        await Task.WhenAll(gamesTask, standingsTask, statLeadersTask);

        var generatedAtUtc = DateTimeOffset.UtcNow;
        var games = await gamesTask;
        var standings = await standingsTask;
        var statLeaders = await statLeadersTask;
        var preview = DailyBriefingPreviewService.Create(
            games,
            standings,
            statLeaders,
            generatedAtUtc);

        if (!azureOpenAiService.IsConfigured)
        {
            return Results.Ok(preview);
        }

        const string cacheKey = "daily-briefing:azure-openai";

        if (cache.TryGetValue(cacheKey, out DailyBriefingDto? cachedBriefing) &&
            cachedBriefing is not null)
        {
            return Results.Ok(cachedBriefing);
        }

        try
        {
            var aiBriefing = await azureOpenAiService.GenerateAsync(
                games,
                standings,
                statLeaders,
                generatedAtUtc,
                cancellationToken);

            cache.Set(cacheKey, aiBriefing, TimeSpan.FromMinutes(15));

            return Results.Ok(aiBriefing);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Azure OpenAI briefing generation failed. Returning preview mode.");

            return Results.Ok(preview with
            {
                DataNotes =
                [
                    .. preview.DataNotes,
                    "Azure OpenAI was unavailable, so the API returned its safe preview fallback."
                ]
            });
        }
    }
    catch (HttpRequestException ex)
    {
        return Results.Problem(
            title: "MLB Stats API unavailable",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
    {
        return Results.Problem(
            title: "MLB Stats API timeout",
            detail: app.Environment.IsDevelopment() ? ex.Message : null,
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.Run();

internal sealed record HealthResponse(
    string Service,
    string Status,
    string Environment,
    DeploymentInfo Deployment,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<HealthCheckItem> Checks);

internal sealed record HealthCheckItem(
    string Name,
    string Status,
    string Description);

internal sealed record DeploymentInfo(
    string Version,
    string BuildId,
    string ImageTag);
