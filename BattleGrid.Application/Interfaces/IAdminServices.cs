using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Application.Interfaces
{
    public interface IAdminServices
    {
        Task<GeneralResponseDto> BanPlayerAsync(BanRequestDto dto);
        Task<GeneralResponseDto> UnbanPlayerAsync(UnbanPlayerRequestDto dto);
        Task<GeneralResponseDto> GrantAdminAsync(GrantAdminRequestDto dto);
    }
}