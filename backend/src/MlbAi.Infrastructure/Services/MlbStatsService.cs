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
        var requestUri = $"schedule?sportId=1&date={date:yyyy-MM-dd}&hydrate=probablePitcher,linescore";
        var response = await httpClient.GetFromJsonAsync<MlbScheduleResponse>(requestUri, cancellationToken);

        if (response?.Dates is null || response.Dates.Count == 0)
        {
            return [];
        }

        var games = response.Dates
            .SelectMany(scheduleDate => scheduleDate.Games.Select(game => ToDto(scheduleDate, game)))
            .OrderBy(game => game.GameTimeUtc)
            .ThenBy(game => game.HomeTeam)
            .ToList();

        var detailedGames = await Task.WhenAll(
            games.Select(game => AddBoxScoreDetailsAsync(game, cancellationToken)));

        return detailedGames
            .OrderBy(game => game.GameTimeUtc)
            .ThenBy(game => game.HomeTeam)
            .ToList();
    }

    private async Task<MlbGameDto> AddBoxScoreDetailsAsync(
        MlbGameDto game,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestUri = $"https://statsapi.mlb.com/api/v1.1/game/{game.GamePk}/feed/live";
            var response = await httpClient.GetFromJsonAsync<MlbLiveFeedResponse>(requestUri, cancellationToken);
            var awayBox = response?.LiveData?.Boxscore?.Teams?.Away;
            var homeBox = response?.LiveData?.Boxscore?.Teams?.Home;

            var awayPitchers = BuildPitcherLines(awayBox);
            var homePitchers = BuildPitcherLines(homeBox);
            var awayBattingLeaders = BuildBattingLeaders(awayBox, game.AwayTeam);
            var homeBattingLeaders = BuildBattingLeaders(homeBox, game.HomeTeam);
            var homeRunHitters = awayBattingLeaders
                .Concat(homeBattingLeaders)
                .Where(player => player.HomeRuns.GetValueOrDefault() > 0)
                .OrderByDescending(player => player.HomeRuns)
                .ThenByDescending(player => player.Rbi)
                .ThenBy(player => player.Name)
                .ToList();

            return game with
            {
                AwayPitchers = awayPitchers,
                HomePitchers = homePitchers,
                AwayBattingLeaders = awayBattingLeaders,
                HomeBattingLeaders = homeBattingLeaders,
                HomeRunHitters = homeRunHitters,
                Highlights = BuildHighlights(
                    game,
                    awayPitchers,
                    homePitchers,
                    awayBattingLeaders,
                    homeBattingLeaders,
                    homeRunHitters)
            };
        }
        catch (HttpRequestException)
        {
            return game;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return game;
        }
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
            HomeScore: game.Teams?.Home?.Score,
            AwayRecord: FormatRecord(game.Teams?.Away?.LeagueRecord),
            HomeRecord: FormatRecord(game.Teams?.Home?.LeagueRecord),
            AwayProbablePitcher: game.Teams?.Away?.ProbablePitcher?.FullName,
            HomeProbablePitcher: game.Teams?.Home?.ProbablePitcher?.FullName,
            AwayWinner: game.Teams?.Away?.IsWinner,
            HomeWinner: game.Teams?.Home?.IsWinner,
            VenueId: game.Venue?.Id,
            VenueName: game.Venue?.Name,
            GameType: FormatGameType(game.GameType),
            DayNight: FormatTitleCase(game.DayNight),
            ScheduledInnings: game.ScheduledInnings ?? game.Linescore?.ScheduledInnings,
            GamesInSeries: game.GamesInSeries,
            SeriesGameNumber: game.SeriesGameNumber,
            SeriesDescription: game.SeriesDescription,
            DoubleHeader: FormatDoubleHeader(game.DoubleHeader),
            CurrentInning: game.Linescore?.CurrentInning,
            CurrentInningOrdinal: game.Linescore?.CurrentInningOrdinal,
            InningHalf: game.Linescore?.InningHalf ?? game.Linescore?.InningState,
            Balls: game.Linescore?.Balls,
            Strikes: game.Linescore?.Strikes,
            Outs: game.Linescore?.Outs,
            AwayHits: game.Linescore?.Teams?.Away?.Hits,
            AwayErrors: game.Linescore?.Teams?.Away?.Errors,
            HomeHits: game.Linescore?.Teams?.Home?.Hits,
            HomeErrors: game.Linescore?.Teams?.Home?.Errors);
    }

    private static string? FormatRecord(MlbLeagueRecord? record)
    {
        return record is null
            ? null
            : $"{record.Wins}-{record.Losses}";
    }

    private static string? FormatGameType(string? gameType)
    {
        return gameType switch
        {
            "R" => "Regular Season",
            "S" => "Spring Training",
            "F" => "Wild Card",
            "D" => "Division Series",
            "L" => "League Championship",
            "W" => "World Series",
            _ => gameType
        };
    }

    private static string? FormatDoubleHeader(string? doubleHeader)
    {
        return doubleHeader switch
        {
            "N" => "No",
            "Y" => "Yes",
            "S" => "Split",
            _ => doubleHeader
        };
    }

    private static string? FormatTitleCase(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
    }

    private static IReadOnlyList<MlbPitcherLineDto> BuildPitcherLines(MlbBoxScoreTeam? boxScoreTeam)
    {
        if (boxScoreTeam?.Pitchers is null)
        {
            return [];
        }

        return boxScoreTeam.Pitchers
            .Select(boxScoreTeam.TryGetPlayer)
            .Where(player => player?.Stats?.Pitching is not null)
            .Select(player =>
            {
                var pitching = player!.Stats!.Pitching!;

                return new MlbPitcherLineDto(
                    Name: player.Person?.FullName ?? "Unknown Pitcher",
                    InningsPitched: pitching.InningsPitched,
                    Hits: pitching.Hits,
                    Runs: pitching.Runs,
                    EarnedRuns: pitching.EarnedRuns,
                    StrikeOuts: pitching.StrikeOuts,
                    Walks: pitching.BaseOnBalls,
                    Pitches: pitching.NumberOfPitches ?? pitching.PitchesThrown,
                    Summary: pitching.Summary);
            })
            .ToList();
    }

    private static IReadOnlyList<MlbBatterLineDto> BuildBattingLeaders(MlbBoxScoreTeam? boxScoreTeam, string teamName)
    {
        if (boxScoreTeam?.Batters is null)
        {
            return [];
        }

        return boxScoreTeam.Batters
            .Select(boxScoreTeam.TryGetPlayer)
            .Where(player => player?.Stats?.Batting is not null)
            .Select(player =>
            {
                var batting = player!.Stats!.Batting!;

                return new MlbBatterLineDto(
                    Name: player.Person?.FullName ?? "Unknown Batter",
                    Team: teamName,
                    AtBats: batting.AtBats,
                    Runs: batting.Runs,
                    Hits: batting.Hits,
                    Doubles: batting.Doubles,
                    Triples: batting.Triples,
                    HomeRuns: batting.HomeRuns,
                    Rbi: batting.Rbi,
                    Walks: batting.BaseOnBalls,
                    StrikeOuts: batting.StrikeOuts,
                    Summary: batting.Summary);
            })
            .Where(player =>
                player.HomeRuns.GetValueOrDefault() > 0 ||
                player.Rbi.GetValueOrDefault() > 0 ||
                player.Hits.GetValueOrDefault() >= 2 ||
                player.Runs.GetValueOrDefault() >= 2)
            .OrderByDescending(player => player.HomeRuns)
            .ThenByDescending(player => player.Rbi)
            .ThenByDescending(player => player.Hits)
            .ThenBy(player => player.Name)
            .Take(6)
            .ToList();
    }

    private static IReadOnlyList<string> BuildHighlights(
        MlbGameDto game,
        IReadOnlyList<MlbPitcherLineDto> awayPitchers,
        IReadOnlyList<MlbPitcherLineDto> homePitchers,
        IReadOnlyList<MlbBatterLineDto> awayBattingLeaders,
        IReadOnlyList<MlbBatterLineDto> homeBattingLeaders,
        IReadOnlyList<MlbBatterLineDto> homeRunHitters)
    {
        var highlights = new List<string>();

        if (homeRunHitters.Count > 0)
        {
            var homeRuns = string.Join(", ", homeRunHitters.Select(player => $"{player.Name} ({player.Team})"));
            highlights.Add($"Home runs: {homeRuns}");
        }

        var bestPitchingLine = awayPitchers
            .Concat(homePitchers)
            .Where(player => player.StrikeOuts.GetValueOrDefault() >= 5 || player.EarnedRuns.GetValueOrDefault() == 0)
            .OrderBy(player => player.EarnedRuns.GetValueOrDefault())
            .ThenByDescending(player => player.StrikeOuts)
            .FirstOrDefault();

        if (bestPitchingLine is not null)
        {
            var pitchingSummary = bestPitchingLine.Summary ?? $"{bestPitchingLine.InningsPitched} IP";
            highlights.Add($"{bestPitchingLine.Name}: {pitchingSummary}");
        }

        var bestBatter = awayBattingLeaders
            .Concat(homeBattingLeaders)
            .OrderByDescending(player => player.Rbi)
            .ThenByDescending(player => player.Hits)
            .FirstOrDefault();

        if (bestBatter is not null)
        {
            var battingSummary = bestBatter.Summary ?? $"{bestBatter.Hits} H, {bestBatter.Rbi} RBI";
            highlights.Add($"{bestBatter.Name}: {battingSummary}");
        }

        if (awayPitchers.Count > 0 || homePitchers.Count > 0)
        {
            highlights.Add($"{game.AwayTeam} used {awayPitchers.Count} pitcher{Pluralize(awayPitchers.Count)}; {game.HomeTeam} used {homePitchers.Count} pitcher{Pluralize(homePitchers.Count)}.");
        }

        return highlights;
    }

    private static string Pluralize(int count)
    {
        return count == 1 ? string.Empty : "s";
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
        public string? GameType { get; set; }
        public string? DayNight { get; set; }
        public int? ScheduledInnings { get; set; }
        public int? GamesInSeries { get; set; }
        public int? SeriesGameNumber { get; set; }
        public string? SeriesDescription { get; set; }
        public string? DoubleHeader { get; set; }
        public MlbGameStatus? Status { get; set; }
        public MlbGameTeams? Teams { get; set; }
        public MlbVenue? Venue { get; set; }
        public MlbLinescore? Linescore { get; set; }
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
        public bool? IsWinner { get; set; }
        public MlbLeagueRecord? LeagueRecord { get; set; }
        public MlbPerson? ProbablePitcher { get; set; }
        public MlbTeam? Team { get; set; }
    }

    private sealed class MlbTeam
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class MlbVenue
    {
        public int? Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class MlbLeagueRecord
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
    }

    private sealed class MlbPerson
    {
        public string? FullName { get; set; }
    }

    private sealed class MlbLinescore
    {
        public int? CurrentInning { get; set; }
        public string? CurrentInningOrdinal { get; set; }
        public string? InningState { get; set; }
        public string? InningHalf { get; set; }
        public int? ScheduledInnings { get; set; }
        public int? Balls { get; set; }
        public int? Strikes { get; set; }
        public int? Outs { get; set; }
        public MlbLinescoreTeams? Teams { get; set; }
    }

    private sealed class MlbLinescoreTeams
    {
        public MlbLineScoreTeam? Away { get; set; }
        public MlbLineScoreTeam? Home { get; set; }
    }

    private sealed class MlbLineScoreTeam
    {
        public int? Hits { get; set; }
        public int? Errors { get; set; }
    }

    private sealed class MlbLiveFeedResponse
    {
        public MlbLiveData? LiveData { get; set; }
    }

    private sealed class MlbLiveData
    {
        public MlbBoxscore? Boxscore { get; set; }
    }

    private sealed class MlbBoxscore
    {
        public MlbBoxScoreTeams? Teams { get; set; }
    }

    private sealed class MlbBoxScoreTeams
    {
        public MlbBoxScoreTeam? Away { get; set; }
        public MlbBoxScoreTeam? Home { get; set; }
    }

    private sealed class MlbBoxScoreTeam
    {
        public List<int> Batters { get; set; } = [];
        public List<int> Pitchers { get; set; } = [];
        public Dictionary<string, MlbBoxScorePlayer> Players { get; set; } = [];

        public MlbBoxScorePlayer? TryGetPlayer(int playerId)
        {
            return Players.TryGetValue($"ID{playerId}", out var player)
                ? player
                : null;
        }
    }

    private sealed class MlbBoxScorePlayer
    {
        public MlbPerson? Person { get; set; }
        public MlbPlayerStats? Stats { get; set; }
    }

    private sealed class MlbPlayerStats
    {
        public MlbBattingStats? Batting { get; set; }
        public MlbPitchingStats? Pitching { get; set; }
    }

    private sealed class MlbBattingStats
    {
        public string? Summary { get; set; }
        public int? Runs { get; set; }
        public int? Doubles { get; set; }
        public int? Triples { get; set; }
        public int? HomeRuns { get; set; }
        public int? StrikeOuts { get; set; }
        public int? BaseOnBalls { get; set; }
        public int? Hits { get; set; }
        public int? AtBats { get; set; }
        public int? Rbi { get; set; }
    }

    private sealed class MlbPitchingStats
    {
        public string? Summary { get; set; }
        public int? Runs { get; set; }
        public int? HomeRuns { get; set; }
        public int? StrikeOuts { get; set; }
        public int? BaseOnBalls { get; set; }
        public int? Hits { get; set; }
        public int? NumberOfPitches { get; set; }
        public string? InningsPitched { get; set; }
        public int? EarnedRuns { get; set; }
        public int? PitchesThrown { get; set; }
    }
}
