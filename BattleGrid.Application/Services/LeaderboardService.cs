using BattleGrid.Application.Helpers;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BattleGrid.Application.Services;

public class LeaderboardService : ILeaderboardService
{
    private readonly BattleGridDbContext _context;
    private readonly IPlayerStatSeasonService _playerStatSeason;

    public LeaderboardService(
        BattleGridDbContext context,
        IPlayerStatSeasonService playerStatSeason)
    {
        _context = context;
        _playerStatSeason = playerStatSeason;
    }

    public async Task<LeaderboardListResponseDto> GetCurrentSeasonLeaderboardAsync(
        int? viewerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var currentSeasonNo = await _playerStatSeason.GetGlobalCurrentSeasonNoAsync(cancellationToken);

        var ranked = await LoadCurrentSeasonRankedAsync(currentSeasonNo, cancellationToken);

        if (viewerUserId is { } viewerId && ranked.All(e => e.UserId != viewerId))
        {
            await _playerStatSeason.EnsureMatchmakingRatingAsync(viewerId, cancellationToken);
            ranked = await LoadCurrentSeasonRankedAsync(currentSeasonNo, cancellationToken);
        }

        LeaderboardWindowBuilder.AssignRanks(ranked);

        var entries = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId);
        return ToResponse(entries);
    }

    public async Task<LeaderboardListResponseDto> GetAllTimeLeaderboardAsync(
        int? viewerUserId = null,
        CancellationToken cancellationToken = default)
    {
        var rawStats = await _context.PlayerStat
            .Join(
                _context.User,
                ps => ps.UserID,
                u => u.UserID,
                (ps, u) => new { ps, u })
            .ToListAsync(cancellationToken);

        var ranked = LeaderboardAllTimeBuilder.BuildRankedList(
            rawStats.Select(x => (x.ps, x.u.UserName)));

        LeaderboardWindowBuilder.AssignRanks(ranked);

        var entries = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId);
        return ToResponse(entries);
    }

    private async Task<List<LeaderboardEntryDto>> LoadCurrentSeasonRankedAsync(
        int currentSeasonNo,
        CancellationToken cancellationToken) =>
        await _context.PlayerStat
            .Where(ps => ps.SeasonNo == currentSeasonNo)
            .Join(
                _context.User,
                ps => ps.UserID,
                u => u.UserID,
                (ps, u) => new LeaderboardEntryDto
                {
                    UserId = u.UserID,
                    UserName = u.UserName,
                    Rating = ps.Rating,
                    MatchesPlayed = ps.MatchesPlayed,
                    MatchesWon = ps.MatchesWon,
                    WinRate = ps.WinRate,
                    SeasonNo = ps.SeasonNo
                })
            .OrderByDescending(x => x.Rating)
            .ThenBy(x => x.UserId)
            .ToListAsync(cancellationToken);

    private static LeaderboardListResponseDto ToResponse(IReadOnlyList<LeaderboardEntryDto> entries) =>
        new()
        {
            Entries = entries,
            PageSize = LeaderboardLimits.PageSize
        };
}