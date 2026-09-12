using System.Globalization;
using System.Net.Http.Json;
using MlbAi.Application.Interfaces;
using MlbAi.Application.Models;

namespace MlbAi.Infrastructure.Services;

public sealed class MlbStatsService(HttpClient httpClient) : IMlbService
{
    private const int AmericanLeagueId = 103;
    private const int NationalLeagueId = 104;
    private static readonly IReadOnlyDictionary<string, MlbLeaderCategoryMeta> LeaderCategoryMeta =
        new Dictionary<string, MlbLeaderCategoryMeta>
        {
            ["battingAverage"] = new("Batting Average", "hitting", "AVG", 1),
            ["homeRuns"] = new("Home Runs", "hitting", "HR", 2),
            ["runsBattedIn"] = new("RBI", "hitting", "RBI", 3),
            ["stolenBases"] = new("Stolen Bases", "hitting", "SB", 4),
            ["wins"] = new("Wins", "pitching", "W", 5),
            ["earnedRunAverage"] = new("ERA", "pitching", "ERA", 6),
            ["strikeouts"] = new("Strikeouts", "pitching", "SO", 7),
            ["walksAndHitsPerInningPitched"] = new("WHIP", "pitching", "WHIP", 8)
        };

    public async Task<IReadOnlyList<MlbGameDto>> GetTodayGamesAsync(CancellationToken cancellationToken = default)
    {
        var today = GetTaipeiToday();
        return await GetGamesByTaipeiDateAsync(today, cancellationToken);
    }

    public async Task<MlbStandingsDto> GetStandingsAsync(CancellationToken cancellationToken = default)
    {
        var season = GetTaipeiToday().Year;
        var requestUri = $"standings?leagueId={AmericanLeagueId},{NationalLeagueId}&season={season}&standingsTypes=regularSeason,wildCard";
        var response = await httpClient.GetFromJsonAsync<MlbStandingsResponse>(requestUri, cancellationToken);
        var records = response?.Records ?? [];
        var regularSeasonRecords = records
            .Where(record => string.Equals(record.StandingsType, "regularSeason", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var wildCardRecords = records
            .Where(record => string.Equals(record.StandingsType, "wildCard", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var leagues = regularSeasonRecords
            .GroupBy(record => record.League?.Id ?? 0)
            .Where(group => group.Key is AmericanLeagueId or NationalLeagueId)
            .OrderBy(group => GetLeagueOrder(group.Key))
            .Select(group => new MlbLeagueStandingsDto(
                LeagueId: group.Key,
                LeagueName: GetLeagueName(group.Key),
                Divisions: group
                    .OrderBy(record => GetDivisionOrder(record.Division?.Id))
                    .Select(record => new MlbDivisionStandingsDto(
                        DivisionId: record.Division?.Id ?? 0,
                        DivisionName: GetDivisionName(record.Division?.Id, record.Division?.Name),
                        Teams: BuildTeamStandings(record.TeamRecords, useWildCardRank: false)))
                    .ToList()))
            .ToList();
        var wildCards = wildCardRecords
            .Where(record => (record.League?.Id ?? 0) is AmericanLeagueId or NationalLeagueId)
            .OrderBy(record => GetLeagueOrder(record.League?.Id ?? 0))
            .Select(record => new MlbWildCardStandingsDto(
                LeagueId: record.League?.Id ?? 0,
                LeagueName: GetLeagueName(record.League?.Id ?? 0),
                Teams: BuildTeamStandings(record.TeamRecords, useWildCardRank: true)))
            .ToList();
        var lastUpdatedUtc = records
            .Select(record => ParseDateTimeOffset(record.LastUpdated))
            .Where(value => value is not null)
            .MaxBy(value => value);

        return new MlbStandingsDto(
            Leagues: leagues,
            WildCards: wildCards,
            LastUpdatedUtc: lastUpdatedUtc);
    }

    public async Task<MlbStatLeadersDto> GetStatLeadersAsync(CancellationToken cancellationToken = default)
    {
        var season = GetTaipeiToday().Year;
        var hittingRequestUri = $"stats/leaders?leaderCategories=battingAverage,homeRuns,runsBattedIn,stolenBases&season={season}&sportId=1&statGroup=hitting&limit=3";
        var pitchingRequestUri = $"stats/leaders?leaderCategories=wins,earnedRunAverage,strikeouts,walksAndHitsPerInningPitched&season={season}&sportId=1&statGroup=pitching&limit=3";
        var responses = await Task.WhenAll(
            httpClient.GetFromJsonAsync<MlbStatLeadersResponse>(hittingRequestUri, cancellationToken),
            httpClient.GetFromJsonAsync<MlbStatLeadersResponse>(pitchingRequestUri, cancellationToken));
        var categories = responses
            .Where(response => response?.LeagueLeaders is not null)
            .SelectMany(response => response!.LeagueLeaders)
            .Where(category =>
                !string.IsNullOrWhiteSpace(category.LeaderCategory) &&
                LeaderCategoryMeta.TryGetValue(category.LeaderCategory, out var meta) &&
                string.Equals(category.StatGroup, meta.StatGroup, StringComparison.OrdinalIgnoreCase))
            .Select(ToLeaderCategoryDto)
            .OrderBy(category => LeaderCategoryMeta[category.CategoryKey].Order)
            .ToList();

        return new MlbStatLeadersDto(categories);
    }

    private async Task<IReadOnlyList<MlbGameDto>> GetGamesByTaipeiDateAsync(
        DateOnly taipeiDate,
        CancellationToken cancellationToken)
    {
        var (startDate, endDate) = GetMlbDateRangeForTaipeiDate(taipeiDate);
        var requestUri = $"schedule?sportId=1&startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}&hydrate=probablePitcher,linescore";
        var response = await httpClient.GetFromJsonAsync<MlbScheduleResponse>(requestUri, cancellationToken);

        if (response?.Dates is null || response.Dates.Count == 0)
        {
            return [];
        }

        var games = response.Dates
            .SelectMany(scheduleDate => scheduleDate.Games.Select(game => ToDto(scheduleDate, game)))
            .Where(game => IsOnTaipeiDate(game.GameTimeUtc, taipeiDate))
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
            ?? GetTaipeiToday();

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
            .Select(playerId => new { PlayerId = playerId, Player = boxScoreTeam.TryGetPlayer(playerId) })
            .Where(playerLine => playerLine.Player?.Stats?.Pitching is not null)
            .Select(playerLine =>
            {
                var player = playerLine.Player!;
                var pitching = player.Stats!.Pitching!;

                return new MlbPitcherLineDto(
                    PlayerId: player.Person?.Id ?? playerLine.PlayerId,
                    Name: player.Person?.FullName ?? "Unknown Pitcher",
                    InningsPitched: pitching.InningsPitched,
                    Hits: pitching.Hits,
                    Runs: pitching.Runs,
                    EarnedRuns: pitching.EarnedRuns,
                    StrikeOuts: pitching.StrikeOuts,
                    Walks: pitching.BaseOnBalls,
                    Pitches: pitching.NumberOfPitches ?? pitching.PitchesThrown,
                    SeasonWins: player.SeasonStats?.Pitching?.Wins,
                    SeasonLosses: player.SeasonStats?.Pitching?.Losses,
                    SeasonEra: player.SeasonStats?.Pitching?.Era,
                    SeasonWhip: player.SeasonStats?.Pitching?.Whip,
                    SeasonStrikeOuts: player.SeasonStats?.Pitching?.StrikeOuts,
                    SeasonInningsPitched: player.SeasonStats?.Pitching?.InningsPitched,
                    SeasonSaves: player.SeasonStats?.Pitching?.Saves,
                    SeasonGamesPitched: player.SeasonStats?.Pitching?.GamesPitched,
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
            .Select(playerId => new { PlayerId = playerId, Player = boxScoreTeam.TryGetPlayer(playerId) })
            .Where(playerLine => playerLine.Player?.Stats?.Batting is not null)
            .Select(playerLine =>
            {
                var player = playerLine.Player!;
                var batting = player.Stats!.Batting!;

                return new MlbBatterLineDto(
                    PlayerId: player.Person?.Id ?? playerLine.PlayerId,
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
                    SeasonAverage: player.SeasonStats?.Batting?.Avg,
                    SeasonOnBasePercentage: player.SeasonStats?.Batting?.Obp,
                    SeasonSluggingPercentage: player.SeasonStats?.Batting?.Slg,
                    SeasonOps: player.SeasonStats?.Batting?.Ops,
                    SeasonHomeRuns: player.SeasonStats?.Batting?.HomeRuns,
                    SeasonRbi: player.SeasonStats?.Batting?.Rbi,
                    SeasonHits: player.SeasonStats?.Batting?.Hits,
                    SeasonStolenBases: player.SeasonStats?.Batting?.StolenBases,
                    Summary: batting.Summary);
            })
            .Where(HasGameBattingLine)
            .OrderByDescending(player => player.HomeRuns)
            .ThenByDescending(player => player.Rbi)
            .ThenByDescending(player => player.Hits)
            .ThenBy(player => player.Name)
            .ToList();
    }

    private static bool HasGameBattingLine(MlbBatterLineDto player)
    {
        return !string.IsNullOrWhiteSpace(player.Summary) ||
            player.AtBats.HasValue ||
            player.Runs.HasValue ||
            player.Hits.HasValue ||
            player.HomeRuns.HasValue ||
            player.Rbi.HasValue ||
            player.Walks.HasValue ||
            player.StrikeOuts.HasValue;
    }

    private static IReadOnlyList<MlbTeamStandingDto> BuildTeamStandings(
        IReadOnlyList<MlbStandingTeamRecord> teamRecords,
        bool useWildCardRank)
    {
        return teamRecords
            .OrderBy(record => ParseRank(useWildCardRank ? record.WildCardRank : record.DivisionRank))
            .ThenBy(record => record.Team?.Name)
            .Select(record => new MlbTeamStandingDto(
                TeamId: record.Team?.Id,
                TeamName: record.Team?.Name ?? "Unknown Team",
                Wins: record.Wins ?? record.LeagueRecord?.Wins ?? 0,
                Losses: record.Losses ?? record.LeagueRecord?.Losses ?? 0,
                WinningPercentage: record.WinningPercentage ?? record.LeagueRecord?.Pct,
                Rank: useWildCardRank ? record.WildCardRank : record.DivisionRank,
                GamesBack: record.GamesBack,
                WildCardGamesBack: record.WildCardGamesBack,
                Streak: record.Streak?.StreakCode,
                LastTen: FormatLastTen(record.Records?.SplitRecords),
                RunDifferential: record.RunDifferential))
            .ToList();
    }

    private static MlbStatLeaderCategoryDto ToLeaderCategoryDto(MlbLeaderCategory category)
    {
        var categoryKey = category.LeaderCategory!;
        var meta = LeaderCategoryMeta[categoryKey];
        var leaders = category.Leaders
            .Where(leader => leader.Person is not null)
            .OrderBy(leader => leader.Rank)
            .ThenBy(leader => leader.Person?.FullName)
            .Take(3)
            .Select(leader => new MlbStatLeaderDto(
                Rank: leader.Rank,
                Value: leader.Value ?? "-",
                PlayerId: leader.Person?.Id,
                PlayerName: leader.Person?.FullName ?? "Unknown Player",
                TeamId: leader.Team?.Id,
                TeamName: leader.Team?.Name ?? "Unknown Team",
                LeagueName: leader.League?.Name))
            .ToList();

        return new MlbStatLeaderCategoryDto(
            CategoryKey: categoryKey,
            Label: meta.Label,
            StatGroup: meta.StatGroup,
            Unit: meta.Unit,
            Leaders: leaders);
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

    private static int GetLeagueOrder(int leagueId)
    {
        return leagueId switch
        {
            AmericanLeagueId => 1,
            NationalLeagueId => 2,
            _ => 99
        };
    }

    private static string GetLeagueName(int leagueId)
    {
        return leagueId switch
        {
            AmericanLeagueId => "American League",
            NationalLeagueId => "National League",
            _ => "MLB"
        };
    }

    private static int GetDivisionOrder(int? divisionId)
    {
        return divisionId switch
        {
            201 or 204 => 1,
            202 or 205 => 2,
            200 or 203 => 3,
            _ => 99
        };
    }

    private static string GetDivisionName(int? divisionId, string? fallbackName)
    {
        return divisionId switch
        {
            201 => "AL East",
            202 => "AL Central",
            200 => "AL West",
            204 => "NL East",
            205 => "NL Central",
            203 => "NL West",
            _ => fallbackName ?? "Division"
        };
    }

    private static int ParseRank(string? rank)
    {
        return int.TryParse(rank, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedRank)
            ? parsedRank
            : int.MaxValue;
    }

    private static string? FormatLastTen(IReadOnlyList<MlbSplitRecord>? splitRecords)
    {
        var lastTen = splitRecords?.FirstOrDefault(record => record.Type == "lastTen");
        return lastTen is null
            ? null
            : $"{lastTen.Wins}-{lastTen.Losses}";
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

    private static bool IsOnTaipeiDate(DateTimeOffset? gameTimeUtc, DateOnly taipeiDate)
    {
        if (gameTimeUtc is null)
        {
            return false;
        }

        var taipeiTime = TimeZoneInfo.ConvertTime(gameTimeUtc.Value, GetTaipeiTimeZone());
        return DateOnly.FromDateTime(taipeiTime.DateTime) == taipeiDate;
    }

    private static (DateOnly StartDate, DateOnly EndDate) GetMlbDateRangeForTaipeiDate(DateOnly taipeiDate)
    {
        var taipeiTimeZone = GetTaipeiTimeZone();
        var easternTimeZone = GetEasternTimeZone();
        var taipeiStartTime = taipeiDate.ToDateTime(TimeOnly.MinValue);
        var taipeiEndTime = taipeiDate.AddDays(1).ToDateTime(TimeOnly.MinValue).AddTicks(-1);
        var taipeiStart = new DateTimeOffset(taipeiStartTime, taipeiTimeZone.GetUtcOffset(taipeiStartTime));
        var taipeiEnd = new DateTimeOffset(taipeiEndTime, taipeiTimeZone.GetUtcOffset(taipeiEndTime));
        var easternStart = TimeZoneInfo.ConvertTime(taipeiStart, easternTimeZone);
        var easternEnd = TimeZoneInfo.ConvertTime(taipeiEnd, easternTimeZone);

        return (
            DateOnly.FromDateTime(easternStart.DateTime),
            DateOnly.FromDateTime(easternEnd.DateTime));
    }

    private static DateOnly GetTaipeiToday()
    {
        var utcNow = DateTimeOffset.UtcNow;
        var taipeiNow = TimeZoneInfo.ConvertTime(utcNow, GetTaipeiTimeZone());
        return DateOnly.FromDateTime(taipeiNow.DateTime);
    }

    private static TimeZoneInfo GetTaipeiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");
        }
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

    private sealed record MlbLeaderCategoryMeta(
        string Label,
        string StatGroup,
        string Unit,
        int Order);

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
        public string? Pct { get; set; }
    }

    private sealed class MlbPerson
    {
        public int? Id { get; set; }
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
        public MlbPlayerStats? SeasonStats { get; set; }
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
        public string? Avg { get; set; }
        public string? Obp { get; set; }
        public string? Slg { get; set; }
        public string? Ops { get; set; }
        public int? StolenBases { get; set; }
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
        public string? Era { get; set; }
        public int? Wins { get; set; }
        public int? Losses { get; set; }
        public int? Saves { get; set; }
        public string? Whip { get; set; }
        public int? GamesPitched { get; set; }
    }

    private sealed class MlbStandingsResponse
    {
        public List<MlbStandingsRecord> Records { get; set; } = [];
    }

    private sealed class MlbStandingsRecord
    {
        public string? StandingsType { get; set; }
        public MlbLeagueInfo? League { get; set; }
        public MlbDivisionInfo? Division { get; set; }
        public string? LastUpdated { get; set; }
        public List<MlbStandingTeamRecord> TeamRecords { get; set; } = [];
    }

    private sealed class MlbLeagueInfo
    {
        public int Id { get; set; }
    }

    private sealed class MlbDivisionInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class MlbStandingTeamRecord
    {
        public MlbTeam? Team { get; set; }
        public string? DivisionRank { get; set; }
        public string? WildCardRank { get; set; }
        public string? GamesBack { get; set; }
        public string? WildCardGamesBack { get; set; }
        public MlbLeagueRecord? LeagueRecord { get; set; }
        public MlbStreak? Streak { get; set; }
        public MlbStandingRecords? Records { get; set; }
        public int? Wins { get; set; }
        public int? Losses { get; set; }
        public int? RunDifferential { get; set; }
        public string? WinningPercentage { get; set; }
    }

    private sealed class MlbStreak
    {
        public string? StreakCode { get; set; }
    }

    private sealed class MlbStandingRecords
    {
        public List<MlbSplitRecord> SplitRecords { get; set; } = [];
    }

    private sealed class MlbSplitRecord
    {
        public int Wins { get; set; }
        public int Losses { get; set; }
        public string? Type { get; set; }
    }

    private sealed class MlbStatLeadersResponse
    {
        public List<MlbLeaderCategory> LeagueLeaders { get; set; } = [];
    }

    private sealed class MlbLeaderCategory
    {
        public string? LeaderCategory { get; set; }
        public string? StatGroup { get; set; }
        public List<MlbLeaderEntry> Leaders { get; set; } = [];
    }

    private sealed class MlbLeaderEntry
    {
        public int Rank { get; set; }
        public string? Value { get; set; }
        public MlbTeam? Team { get; set; }
        public MlbLeagueNamedInfo? League { get; set; }
        public MlbPerson? Person { get; set; }
    }

    private sealed class MlbLeagueNamedInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}
