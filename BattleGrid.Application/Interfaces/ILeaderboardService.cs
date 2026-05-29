using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Application.Interfaces;

public interface ILeaderboardService
{
    Task<LeaderboardListResponseDto> GetCurrentSeasonLeaderboardAsync(
        int? viewerUserId = null,
        CancellationToken cancellationToken = default);

    Task<LeaderboardListResponseDto> GetAllTimeLeaderboardAsync(
        int? viewerUserId = null,
        CancellationToken cancellationToken = default);
}