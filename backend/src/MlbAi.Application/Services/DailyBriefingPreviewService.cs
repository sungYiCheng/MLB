using MlbAi.Application.Models;

namespace MlbAi.Application.Services;

public static class DailyBriefingPreviewService
{
    public static DailyBriefingDto Create(
        IReadOnlyList<MlbGameDto> games,
        MlbStandingsDto standings,
        MlbStatLeadersDto statLeaders,
        DateTimeOffset generatedAtUtc)
    {
        var keyGames = games
            .Take(3)
            .Select(game => new DailyBriefingItemDto(
                Title: $"{game.AwayTeam} at {game.HomeTeam}",
                Detail: BuildGameDetail(game)))
            .ToArray();

        var playersToWatch = statLeaders.Categories
            .SelectMany(category => category.Leaders
                .Take(1)
                .Select(leader => new DailyBriefingItemDto(
                    Title: leader.PlayerName,
                    Detail: $"{category.Label} leader at {leader.Value} ({leader.TeamName}).")))
            .DistinctBy(player => player.Title)
            .Take(4)
            .ToArray();

        var rankedTeamCount = standings.Leagues
            .SelectMany(league => league.Divisions)
            .SelectMany(division => division.Teams)
            .Count();

        return new DailyBriefingDto(
            Mode: "preview",
            IsAiGenerated: false,
            Headline: games.Count == 0
                ? "No MLB games are scheduled for Taiwan today"
                : $"{games.Count} MLB matchup{MlbDisplayFormatter.Pluralize(games.Count)} on Taiwan's board",
            Summary: games.Count == 0
                ? "The briefing contract is ready. New game insights will appear when games are scheduled."
                : $"This preview combines today's schedule, {rankedTeamCount} team records, and current league leaders. The next step replaces these rules with a grounded AI model response.",
            KeyGames: keyGames,
            PlayersToWatch: playersToWatch,
            DataNotes:
            [
                "Source data comes from the MLB Stats API.",
                "Preview mode is deterministic and does not call a generative AI model.",
                "The final AI response will keep this same structured contract."
            ],
            GeneratedAtUtc: generatedAtUtc);
    }

    private static string BuildGameDetail(MlbGameDto game)
    {
        var probablePitchers = new[]
        {
            game.AwayProbablePitcher,
            game.HomeProbablePitcher
        }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        return probablePitchers.Length > 0
            ? $"{game.Status}. Probable pitchers: {string.Join(" vs ", probablePitchers)}."
            : $"{game.Status}. Probable pitchers have not both been announced.";
    }
}
