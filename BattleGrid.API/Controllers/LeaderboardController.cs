using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboardService;

    public LeaderboardController(ILeaderboardService leaderboardService)
    {
        _leaderboardService = leaderboardService;
    }

    /// <summary>
    /// Top 100 for the active season (plus a local rank window when the viewer is outside the top 100).
    /// </summary>
    [HttpGet("season")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LeaderboardListResponseDto))]
    public async Task<IActionResult> GetCurrentSeasonLeaderboard(CancellationToken cancellationToken)
    {
        int? viewerUserId = User.TryGetAuthenticatedUserId(out var uid) ? uid : null;
        var result = await _leaderboardService.GetCurrentSeasonLeaderboardAsync(viewerUserId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Top 100 all-time peak ratings (max <c>HighestRating</c> per player; plus a local rank window when the viewer is outside the top 100).
    /// </summary>
    [HttpGet("all-time")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LeaderboardListResponseDto))]
    public async Task<IActionResult> GetAllTimeLeaderboard(CancellationToken cancellationToken)
    {
        int? viewerUserId = User.TryGetAuthenticatedUserId(out var uid) ? uid : null;
        var result = await _leaderboardService.GetAllTimeLeaderboardAsync(viewerUserId, cancellationToken);
        return Ok(result);
    }
}