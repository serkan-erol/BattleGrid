using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Enums;

namespace BattleGrid.Application.Interfaces
{
    public interface IMatchServices
    {
        Task<List<int>> GetPlayersAsync(int matchId);
        Task<GeneralResponseDto> ValidatePlayerAsync(int matchId, int playerId);
        Task<GeneralResponseDto> TrySetMatchStatusAsync(int matchId, MatchStatus status);

        /// <summary> Persists match outcome, rating changes, and all recorded shots (hits/misses only - not duplicate-target attempts). </summary>
        Task<PersistCompletedBattleResponseDto> PersistCompletedBattleAsync(
            int matchId,
            int winnerUserId,
            IReadOnlyList<(int shooterId, 
                           int moveNumber, 
                           int hitX, 
                           int hitY, 
                           bool hit)> 
            moves,
            string? finishReason = null);

        Task<int?> GetResumableMatchIdAsync(int userId);

        Task<GeneralResponseDto> PersistAbandonedMatchAsync(int matchId, string finishReason);

        /// <summary>
        /// Finds matches still in Loading, PlacingShips, or InProgress whose <c>StartedAt</c> is at least
        /// <paramref name="olderThan"/> ago and marks each as <see cref="MatchStatus.Abandoned"/> with a system finish reason.
        /// </summary>
        /// <returns>Number of rows updated from an active state to abandoned (not counting rows already terminal).</returns>
        Task<int> AbandonStaleMatchesAsync(TimeSpan olderThan, CancellationToken cancellationToken = default);

        /// <summary>
        /// If the match is terminal in DB and <paramref name="userId"/> is a player, returns recovery payload; otherwise null.
        /// </summary>
        Task<MatchRecoveryStateDto?> GetMatchRecoveryStateIfTerminalAsync(int matchId, int userId);
    }
}