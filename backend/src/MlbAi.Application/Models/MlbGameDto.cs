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
    int? HomeScore = null);
