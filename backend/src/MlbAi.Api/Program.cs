using MlbAi.Application.Interfaces;
using MlbAi.Infrastructure.Services;

const string appName = "MLB AI Daily API";
const string mlbStatsApiBaseUrl = "https://statsapi.mlb.com/api/v1/";

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHttpClient<IMlbService, MlbStatsService>(client =>
    {
        client.BaseAddress = new Uri(mlbStatsApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
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
            Description: "The MLB Stats API HTTP client has a base URL configured; this check does not call the external MLB API.")
    };

    return Results.Ok(new HealthResponse(
        Service: appName,
        Status: "healthy",
        Environment: environment.EnvironmentName,
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

app.Run();

internal sealed record HealthResponse(
    string Service,
    string Status,
    string Environment,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<HealthCheckItem> Checks);

internal sealed record HealthCheckItem(
    string Name,
    string Status,
    string Description);
