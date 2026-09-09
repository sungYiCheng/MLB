using System.Globalization;
using System.Net.Http.Json;
using MlbAi.Application.Interfaces;
using MlbAi.Application.Models;

namespace MlbAi.Infrastructure.Services;

public sealed class MlbStatsService(HttpClient httpClient) : IMlbService
{
    public async Task<IReadOnlyList<MlbGameDto>> GetTodayGamesAsync(CancellationToken cancellationToken = default)
    {
        var today = GetMlbToday();
        return await GetGamesByDateAsync(today, cancellationToken);
    }

    private async Task<IReadOnlyList<MlbGameDto>> GetGamesByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var requestUri = $"schedule?sportId=1&date={date:yyyy-MM-dd}";
        var response = await httpClient.GetFromJsonAsync<MlbScheduleResponse>(requestUri, cancellationToken);

        if (response?.Dates is null || response.Dates.Count == 0)
        {
            return [];
        }

        return response.Dates
            .SelectMany(scheduleDate => scheduleDate.Games.Select(game => ToDto(scheduleDate, game)))
            .OrderBy(game => game.GameTimeUtc)
            .ThenBy(game => game.HomeTeam)
            .ToList();
    }

    private static MlbGameDto ToDto(MlbScheduleDate scheduleDate, MlbScheduleGame game)
    {
        var gameDate = ParseDateOnly(game.OfficialDate)
            ?? ParseDateOnly(scheduleDate.Date)
            ?? GetMlbToday();

        return new MlbGameDto(
            GamePk: game.GamePk.ToString(CultureInfo.InvariantCulture),
            GameDate: gameDate,
            GameTimeUtc: ParseDateTimeOffset(game.GameDate),
            AwayTeamId: game.Teams?.Away?.Team?.Id,
            AwayTeam: game.Teams?.Away?.Team?.Name ?? "Unknown Away Team",
            HomeTeamId: game.Teams?.Home?.Team?.Id,
            HomeTeam: game.Teams?.Home?.Team?.Name ?? "Unknown Home Team",
            Status: game.Status?.DetailedState ?? game.Status?.AbstractGameState ?? "Unknown",
            AwayScore: game.Teams?.Away?.Score,
            HomeScore: game.Teams?.Home?.Score);
    }

    private static DateOnly? ParseDateOnly(string? value)
    {
        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var dateTime)
            ? dateTime.ToUniversalTime()
            : null;
    }

    private static DateOnly GetMlbToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var easternNow = TimeZoneInfo.ConvertTime(utcNow, GetEasternTimeZone());
        return DateOnly.FromDateTime(easternNow.DateTime);
    }

    private static TimeZoneInfo GetEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }

    private sealed class MlbScheduleResponse
    {
        public List<MlbScheduleDate> Dates { get; set; } = [];
    }

    private sealed class MlbScheduleDate
    {
        public string? Date { get; set; }
        public List<MlbScheduleGame> Games { get; set; } = [];
    }

    private sealed class MlbScheduleGame
    {
        public int GamePk { get; set; }
        public string? GameDate { get; set; }
        public string? OfficialDate { get; set; }
        public MlbGameStatus? Status { get; set; }
        public MlbGameTeams? Teams { get; set; }
    }

    private sealed class MlbGameStatus
    {
        public string? AbstractGameState { get; set; }
        public string? DetailedState { get; set; }
    }

    private sealed class MlbGameTeams
    {
        public MlbGameTeamSlot? Away { get; set; }
        public MlbGameTeamSlot? Home { get; set; }
    }

    private sealed class MlbGameTeamSlot
    {
        public int? Score { get; set; }
        public MlbTeam? Team { get; set; }
    }

    private sealed class MlbTeam
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
    }
}
