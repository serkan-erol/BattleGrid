using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Application.Interfaces
{
    public interface IShipPlacementServices
    {
        Task<GeneralResponseDto> PlaceShipAsync(PlaceShipRequestDto dto);

        /// <summary> Inserts rows in list order within a single transaction (preserves placement order). </summary>
        Task<GeneralResponseDto> SavePlacementsBatchAsync(int matchId, IReadOnlyList<(int PlayerID, int ShipID, int StartX, int StartY, bool IsVertical)> rowsInOrder);
    }
}