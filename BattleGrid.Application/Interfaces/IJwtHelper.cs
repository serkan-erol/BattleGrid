using System.Security.Claims;

namespace BattleGrid.Application.Interfaces
{
    public interface IJwtHelper
    {
        Task<string> GenerateAccessTokenAsync(string loginInfo);
        Task<string> GenerateRefreshTokenAsync();

        ClaimsPrincipal GetPrincipalFromExpiredToken(string token);
        DateTimeOffset GetAccessTokenExpiration();
        DateTimeOffset GetRefreshTokenExpiration();
        ClaimsPrincipal GetPrincipalFromToken(string token);

    }
}