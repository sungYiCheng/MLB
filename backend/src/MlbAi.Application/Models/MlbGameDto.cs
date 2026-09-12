namespace MlbAi.Application.Models;

public sealed record MlbGameDto(
    string GamePk,
    DateOnly GameDate,
    DateTimeOffset? GameTimeUtc,
    int? AwayTeamId,
    string AwayTeam,
    int? HomeTeamId,
    string HomeTeam,
    string Status,
    int? AwayScore = null,
    int? HomeScore = null,
    string? AwayRecord = null,
    string? HomeRecord = null,
    string? AwayProbablePitcher = null,
    string? HomeProbablePitcher = null,
    bool? AwayWinner = null,
    bool? HomeWinner = null,
    int? VenueId = null,
    string? VenueName = null,
    string? GameType = null,
    string? DayNight = null,
    int? ScheduledInnings = null,
    int? GamesInSeries = null,
    int? SeriesGameNumber = null,
    string? SeriesDescription = null,
    string? DoubleHeader = null,
    int? CurrentInning = null,
    string? CurrentInningOrdinal = null,
    string? InningHalf = null,
    int? Balls = null,
    int? Strikes = null,
    int? Outs = null,
    int? AwayHits = null,
    int? AwayErrors = null,
    int? HomeHits = null,
    int? HomeErrors = null,
    IReadOnlyList<MlbPitcherLineDto>? AwayPitchers = null,
    IReadOnlyList<MlbPitcherLineDto>? HomePitchers = null,
    IReadOnlyList<MlbBatterLineDto>? AwayBattingLeaders = null,
    IReadOnlyList<MlbBatterLineDto>? HomeBattingLeaders = null,
    IReadOnlyList<MlbBatterLineDto>? HomeRunHitters = null,
    IReadOnlyList<string>? Highlights = null);

public sealed record MlbPitcherLineDto(
    int? PlayerId,
    string Name,
    string? InningsPitched,
    int? Hits,
    int? Runs,
    int? EarnedRuns,
    int? StrikeOuts,
    int? Walks,
    int? Pitches,
    int? SeasonWins,
    int? SeasonLosses,
    string? SeasonEra,
    string? SeasonWhip,
    int? SeasonStrikeOuts,
    string? SeasonInningsPitched,
    int? SeasonSaves,
    int? SeasonGamesPitched,
    string? Summary);

public sealed record MlbBatterLineDto(
    int? PlayerId,
    string Name,
    string Team,
    int? AtBats,
    int? Runs,
    int? Hits,
    int? Doubles,
    int? Triples,
    int? HomeRuns,
    int? Rbi,
    int? Walks,
    int? StrikeOuts,
    string? SeasonAverage,
    string? SeasonOnBasePercentage,
    string? SeasonSluggingPercentage,
    string? SeasonOps,
    int? SeasonHomeRuns,
    int? SeasonRbi,
    int? SeasonHits,
    int? SeasonStolenBases,
    string? Summary);

public sealed record MlbStandingsDto(
    IReadOnlyList<MlbLeagueStandingsDto> Leagues,
    IReadOnlyList<MlbWildCardStandingsDto> WildCards,
    DateTimeOffset? LastUpdatedUtc);

public sealed record MlbLeagueStandingsDto(
    int LeagueId,
    string LeagueName,
    IReadOnlyList<MlbDivisionStandingsDto> Divisions);

public sealed record MlbDivisionStandingsDto(
    int DivisionId,
    string DivisionName,
    IReadOnlyList<MlbTeamStandingDto> Teams);

public sealed record MlbWildCardStandingsDto(
    int LeagueId,
    string LeagueName,
    IReadOnlyList<MlbTeamStandingDto> Teams);

public sealed record MlbTeamStandingDto(
    int? TeamId,
    string TeamName,
    int Wins,
    int Losses,
    string? WinningPercentage,
    string? Rank,
    string? GamesBack,
    string? WildCardGamesBack,
    string? Streak,
    string? LastTen,
    int? RunDifferential);

public sealed record MlbStatLeadersDto(
    IReadOnlyList<MlbStatLeaderCategoryDto> Categories);

public sealed record MlbStatLeaderCategoryDto(
    string CategoryKey,
    string Label,
    string StatGroup,
    string Unit,
    IReadOnlyList<MlbStatLeaderDto> Leaders);

public sealed record MlbStatLeaderDto(
    int Rank,
    string Value,
    int? PlayerId,
    string PlayerName,
    int? TeamId,
    string TeamName,
    string? LeagueName);
