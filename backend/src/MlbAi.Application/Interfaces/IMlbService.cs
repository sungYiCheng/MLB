using MlbAi.Application.Models;

namespace MlbAi.Application.Interfaces;

public interface IMlbService
{
    Task<IReadOnlyList<MlbGameDto>> GetTodayGamesAsync(CancellationToken cancellationToken = default);

    Task<MlbStandingsDto> GetStandingsAsync(CancellationToken cancellationToken = default);

    Task<MlbStatLeadersDto> GetStatLeadersAsync(CancellationToken cancellationToken = default);
}
