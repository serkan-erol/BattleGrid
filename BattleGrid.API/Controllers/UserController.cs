using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly IUserServices _userServices;

    public UserController(IUserServices userService)
    {
        _userServices = userService;
    }

    [HttpGet("all")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<PagedUsersResponseDto>> GetAllUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _userServices.GetUsersPageAsync(page, pageSize);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occured while retrieving the users: {ex.Message}");
        }
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult> GetUserById(int userId)
    {
        try
        {
            var user = await _userServices.GetByIdAsync(userId);

            if (user == null)
                return NotFound("User not found!");

            return Ok(user);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occured while retrieving the user: {ex.Message}");
        }
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponseDto>> GetMe()
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var user = await _userServices.GetByIdAsync(uid);
        if (user is null || !user.IsActive)
            return NotFound();

        return Ok(user);
    }

    [HttpPatch("email")]
    public async Task<ActionResult> ChangeEmail(
        [FromBody] ChangeEmailRequestDto dto,
        [FromServices] IAuthServices auth)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var (result, updatedUser) = await auth.ChangeEmailAsync(uid, dto);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { result.Message, user = updatedUser });
    }

    [HttpPatch("username")]
    public async Task<ActionResult> ChangeUsername(
        [FromBody] ChangeUsernameRequestDto dto,
        [FromServices] IAuthServices auth)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var (result, updatedUser) = await auth.ChangeUsernameAsync(uid, dto);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { result.Message, user = updatedUser });
    }

    [HttpPatch("deactivate")]
    public async Task<ActionResult> DeactivateAccount(
        [FromBody] DeactivateAccountRequestDto dto,
        [FromServices] IAuthServices auth)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var result = await auth.DeactivateAccountAsync(uid, dto);
        if (!result.Success)
            return BadRequest(result.Message);

        return Ok(new { result.Message });
    }

    /// <summary>Active ban duration for the authenticated user (dashboard status).</summary>
    [HttpGet("ban-status")]
    public async Task<ActionResult<UserBanStatusResponseDto>> GetMyBanStatus(
        [FromServices] IBanListServices banList)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        var status = await banList.GetBanStatusForPlayerAsync(uid);
        return Ok(status);
    }

    /// <summary> Competitive stats for the authenticated user for the active global season. </summary>
    [HttpGet("stats/current-season")]
    public async Task<ActionResult<PlayerSeasonStatsResponseDto>> GetMyCurrentSeasonStats(
        [FromServices] IPlayerStatSeasonService playerStatSeason)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        try
        {
            var stats = await playerStatSeason.GetCurrentSeasonStatsAsync(uid);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occured while retrieving season stats: {ex.Message}");
        }
    }

    /// <summary> Lookup by username or email (used before login). </summary>
    [HttpGet("{loginInfo}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetByLoginInfo(string loginInfo)
    {
        try
        {
            var user = await _userServices.GetByLoginInfoAsync(loginInfo);

            if (user == null)
                return NotFound("User not found!");

            return Ok(user);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"An error occured while retrieving the user info: {ex.Message}");
        }
    }
}