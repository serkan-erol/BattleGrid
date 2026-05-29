using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ShipPlacementController : ControllerBase
{
    private readonly IShipPlacementServices _shipPlacementServices;
    private readonly IMatchServices _matchServices;

    public ShipPlacementController(
        IShipPlacementServices shipPlacementServices,
        IMatchServices matchServices)
    {
        _shipPlacementServices = shipPlacementServices;
        _matchServices = matchServices;
    }

    [HttpPost("placeShip")]
    public async Task<IActionResult> PlaceShip([FromBody] PlaceShipRequestDto dto)
    {
        if (!User.TryGetAuthenticatedUserId(out var uid))
            return Unauthorized();

        try
        {
            dto.PlayerID = uid;

            var isPlayer = await _matchServices.ValidatePlayerAsync(dto.MatchID, uid);

            if (!isPlayer.Success)
                return Unauthorized($"You can not place this ship: {isPlayer.Message}");

            var result = await _shipPlacementServices.PlaceShipAsync(dto);

            if (!result.Success)
                return BadRequest($"An error occured while placing the ship: {result.Message}");

            return Ok(result.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Ship placement error: {ex.Message}");
        }
    }
}