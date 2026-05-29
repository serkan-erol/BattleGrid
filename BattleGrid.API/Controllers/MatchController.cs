using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class MatchController : ControllerBase
{
    [HttpGet("resumable")]
    public async Task<ActionResult<ResumableMatchResponseDto>> Resumable([FromServices] IMatchServices matches)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var mid = await matches.GetResumableMatchIdAsync(uid);
        if (mid is null)
            return NoContent();

        return Ok(new ResumableMatchResponseDto { MatchId = mid.Value });
    }

    /// <summary>
    /// When the match row is already terminal (won / abandoned), returns state so the Blazor client can
    /// show the post-match UI after a UI-only restart (missed <c>MatchEnded</c> over SignalR).
    /// </summary>
    [HttpGet("{matchId:int}/recovery")]
    public async Task<ActionResult<MatchRecoveryStateDto>> GetRecovery(int matchId, [FromServices] IMatchServices matches)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var dto = await matches.GetMatchRecoveryStateIfTerminalAsync(matchId, uid);
        if (dto is null)
            return NoContent();

        return Ok(dto);
    }

    [HttpGet("history")]
    public async Task<ActionResult<MatchHistoryResponseDto>> History([FromServices] IReplayServices replay, [FromQuery] int limit = 20)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var dto = await replay.GetRecentMatchHistoryAsync(uid, limit);

        return Ok(dto);
    }

    [HttpGet("{matchId:int}/replay")]
    public async Task<ActionResult<MatchReplayResponseDto>> GetReplay(int matchId, [FromServices] IReplayServices replay)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var dto = await replay.GetMatchReplayAsync(matchId, uid);

        if (dto is null)
            return NotFound();

        return Ok(dto);
    }
}