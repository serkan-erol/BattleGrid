using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Infrastructure.Data;

namespace BattleGrid.Application.Services
{
    public class ShipPlacementServices : IShipPlacementServices
    {
        private readonly BattleGridDbContext _context;

        public ShipPlacementServices(BattleGridDbContext context)
        {
            _context = context;
        }

        //aaa TODO: PlayerID and (if possible) MatchID should be taken from route and be hidden from user with [JsonIgnore]
        // PlayerID will already be saved at localStorage of the browser as UserID
        // We will see if we can save the MatchID to sessionStorage once SignalR adds the user to a match hub.
        // Last part has to wait till we implement the SignalR and hubs
        public async Task<GeneralResponseDto> PlaceShipAsync(PlaceShipRequestDto dto)
        {

            //aaa TODO add a check to make sure player can not place more than allowed number of ships of any ship type
            // Check PlayerID, MatchID, and ShipID all together and compare it to max allowed numbers of each type with unique ShipID

            var result = await _context.ShipPlacement.AddAsync(new ShipPlacement
            {
                PlayerID = dto.PlayerID,
                MatchID = dto.MatchID,
                ShipID = dto.ShipID,
                StartX = dto.StartX,
                StartY = dto.StartY,
                IsVertical = dto.IsVertical
            });

            await _context.SaveChangesAsync();

            if (result == null)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "An error occured during ship placement!"
                };
            }

            return new GeneralResponseDto
            {
                Success = true,
                Message = "Ship placed successfully."
            };
        }

        public async Task<GeneralResponseDto> SavePlacementsBatchAsync(
            int matchId,
            IReadOnlyList<(int PlayerID, int ShipID, int StartX, int StartY, bool IsVertical)> rowsInOrder)
        {
            foreach (var row in rowsInOrder)
            {
                await _context.ShipPlacement.AddAsync(new ShipPlacement
                {
                    PlayerID = row.PlayerID,
                    MatchID = matchId,
                    ShipID = row.ShipID,
                    StartX = row.StartX,
                    StartY = row.StartY,
                    IsVertical = row.IsVertical
                });
            }

            await _context.SaveChangesAsync();

            return new GeneralResponseDto
            {
                Success = true,
                Message = "Placements saved."
            };
        }
    }
}