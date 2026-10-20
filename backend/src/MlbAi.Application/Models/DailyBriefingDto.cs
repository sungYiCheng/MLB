namespace MlbAi.Application.Models;

public sealed record DailyBriefingDto(
    string Mode,
    bool IsAiGenerated,
    string Headline,
    string Summary,
    IReadOnlyList<DailyBriefingItemDto> KeyGames,
    IReadOnlyList<DailyBriefingItemDto> PlayersToWatch,
    IReadOnlyList<string> DataNotes,
    DateTimeOffset GeneratedAtUtc);

public sealed record DailyBriefingItemDto(
    string Title,
    string Detail);
