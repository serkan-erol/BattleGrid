using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Admin")]
public sealed class AdminController : ControllerBase
{
    private readonly IAdminServices _adminServices;
    private readonly IUserServices _userServices;

    public AdminController(IAdminServices adminServices, IUserServices userServices)
    {
        _adminServices = adminServices;
        _userServices = userServices;
    }

    /// <summary>
    /// Ends the current competitive season and starts the next. Creates a new season stat row for the
    /// calling administrator; all other players receive a row when they enter matchmaking.
    /// </summary>
    [HttpPost("season/advance")]
    public async Task<ActionResult<AdvanceSeasonResponseDto>> AdvanceSeason([FromServices] IPlayerStatSeasonService playerStatSeason)
    {
        if (!User.TryGetAuthenticatedUserId(out var adminUserId))
            return Unauthorized();

        var result = await playerStatSeason.AdvanceSeasonAsync(adminUserId);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>Bans a player by username or email. Admin identity is taken from the JWT only.</summary>
    [HttpPost("players/ban")]
    public async Task<ActionResult<GeneralResponseDto>> BanPlayer([FromBody] BanRequestDto dto)
    {
        if (!User.TryGetAuthenticatedUserId(out var adminUserId))
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        dto.AdminID = adminUserId;

        var result = await _adminServices.BanPlayerAsync(dto);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary> Reverts the active ban for a player (temporary or permanent). Admin identity is taken from the JWT only. </summary>
    [HttpPost("players/unban")]
    public async Task<ActionResult<GeneralResponseDto>> UnbanPlayer([FromBody] UnbanPlayerRequestDto dto)
    {
        if (!User.TryGetAuthenticatedUserId(out var adminUserId))
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        dto.AdminID = adminUserId;

        var result = await _adminServices.UnbanPlayerAsync(dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>Grants administrator privileges to a player by username or email.</summary>
    [HttpPost("players/grant-admin")]
    public async Task<ActionResult<GeneralResponseDto>> GrantAdmin([FromBody] GrantAdminRequestDto dto)
    {
        if (!User.TryGetAuthenticatedUserId(out var adminUserId))
            return Unauthorized();

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        dto.AdminID = adminUserId;

        var result = await _adminServices.GrantAdminAsync(dto);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    /// <summary>Operator profile with all season stat rows (admin only).</summary>
    [HttpGet("players/{userId:int}/profile")]
    public async Task<ActionResult<AdminUserProfileResponseDto>> GetPlayerProfile(int userId)
    {
        var profile = await _userServices.GetAdminUserProfileAsync(userId);
        if (profile is null)
            return NotFound();

        return Ok(profile);
    }

    /// <summary>Recent match history for an operator (admin only; end kinds from that player's perspective).</summary>
    [HttpGet("players/{userId:int}/match-history")]
    public async Task<ActionResult<MatchHistoryResponseDto>> GetPlayerMatchHistory(
        int userId,
        [FromServices] IReplayServices replay,
        [FromQuery] int limit = 20)
    {
        if (await _userServices.GetByIdAsync(userId) is null)
            return NotFound();

        var dto = await replay.GetRecentMatchHistoryAsync(userId, limit);
        return Ok(dto);
    }

    /// <summary> Replay data for any match with a decisive outcome (admin only). </summary>
    [HttpGet("matches/{matchId:int}/replay")]
    public async Task<ActionResult<MatchReplayResponseDto>> GetMatchReplay(
        int matchId,
        [FromServices] IReplayServices replay)
    {
        var dto = await replay.GetMatchReplayForAdminAsync(matchId);
        if (dto is null)
            return NotFound();

        return Ok(dto);
    }
}
