using BattleGrid.Application.Interfaces;
using BattleGrid.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;


namespace BattleGrid.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ShipTypeController : ControllerBase
    {
        private readonly BattleGridDbContext _context;
        private readonly IShipTypeServices _shipTypeService;

        public ShipTypeController(BattleGridDbContext context,
                                  IShipTypeServices shipTypeService)
        {
            _context = context;
            _shipTypeService = shipTypeService;
        }

        [HttpGet("all")]
        public async Task<ActionResult> ShipTypeList()
        {
            try
            {
                var shipList = await _shipTypeService.ShipTypeListAsync();

                if (shipList == null)
                {
                    return NotFound("No ships found!");
                }

                return Ok(shipList);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occured while retrieving the ship list: {ex.Message}");
            }
        }
    }
}