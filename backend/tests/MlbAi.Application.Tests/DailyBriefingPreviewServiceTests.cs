using MlbAi.Application.Models;
using MlbAi.Application.Services;
using Xunit;

namespace MlbAi.Application.Tests;

public sealed class DailyBriefingPreviewServiceTests
{
    [Fact]
    public void Create_ReturnsStructuredPreviewFromVerifiedMlbData()
    {
        var generatedAt = new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero);
        var games = new[]
        {
            new MlbGameDto(
                GamePk: "1",
                GameDate: new DateOnly(2026, 10, 20),
                GameTimeUtc: generatedAt,
                AwayTeamId: 147,
                AwayTeam: "Yankees",
                HomeTeamId: 111,
                HomeTeam: "Red Sox",
                Status: "Scheduled",
                AwayProbablePitcher: "Away Starter",
                HomeProbablePitcher: "Home Starter")
        };
        var standings = new MlbStandingsDto(
            Leagues:
            [
                new MlbLeagueStandingsDto(
                    103,
                    "American League",
                    [
                        new MlbDivisionStandingsDto(
                            201,
                            "AL East",
                            [new MlbTeamStandingDto(147, "Yankees", 90, 72, ".556", "1", "-", "-", "W2", "7-3", 42)])
                    ])
            ],
            WildCards: [],
            LastUpdatedUtc: generatedAt);
        var leaders = new MlbStatLeadersDto(
        [
            new MlbStatLeaderCategoryDto(
                "homeRuns",
                "Home Runs",
                "hitting",
                "HR",
                [new MlbStatLeaderDto(1, "51", 592450, "Aaron Judge", 147, "Yankees", "American League")])
        ]);

        var result = DailyBriefingPreviewService.Create(games, standings, leaders, generatedAt);

        Assert.Equal("preview", result.Mode);
        Assert.False(result.IsAiGenerated);
        Assert.Contains("1 MLB matchup", result.Headline);
        Assert.Equal("Yankees at Red Sox", result.KeyGames.Single().Title);
        Assert.Contains("Away Starter vs Home Starter", result.KeyGames.Single().Detail);
        Assert.Equal("Aaron Judge", result.PlayersToWatch.Single().Title);
        Assert.Equal(generatedAt, result.GeneratedAtUtc);
    }

    [Fact]
    public void Create_HandlesDaysWithoutScheduledGames()
    {
        var result = DailyBriefingPreviewService.Create(
            [],
            new MlbStandingsDto([], [], null),
            new MlbStatLeadersDto([]),
            DateTimeOffset.UnixEpoch);

        Assert.Empty(result.KeyGames);
        Assert.Empty(result.PlayersToWatch);
        Assert.Contains("No MLB games", result.Headline);
    }
}
