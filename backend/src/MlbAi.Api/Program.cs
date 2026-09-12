using MlbAi.Application.Interfaces;
using MlbAi.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddHttpClient<IMlbService, MlbStatsService>(client =>
    {
        client.BaseAddress = new Uri("https://statsapi.mlb.com/api/v1/");
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

app.MapGet("/", () => Results.Ok(new { name = "MLB AI Daily API", status = "ready" }));

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
