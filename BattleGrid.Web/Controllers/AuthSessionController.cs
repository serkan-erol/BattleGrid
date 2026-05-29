using BattleGrid.Contracts;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Web.Extensions;
using BattleGrid.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace BattleGrid.Web.Controllers;

[ApiController]
[Route("auth/session")]
public class AuthSessionController : ControllerBase
{
    /// <summary>
    /// Aligns with typical JWT bearer validation: accept tokens slightly past <c>exp</c>
    /// if resource server clock is behind (see Microsoft.IdentityModel Validators).
    /// </summary>
    private static readonly TimeSpan AccessTokenValidationClockSkew = TimeSpan.FromMinutes(1);

    private readonly HttpClient _api;

    public AuthSessionController(HttpClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Restores session from token cookies only. Calls <c>/api/Auth/refresh</c> only when the
    /// access JWT is missing or no longer within the validation window (exp + clock skew).
    /// User profile fields come from JWT claims - no round trip to User API on every page load.
    /// </summary>
    [HttpGet("read")]
    public async Task<IActionResult> Read()
    {
        if (!Request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var refreshToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return NoContent();
        }

        Request.Cookies.TryGetValue(AuthCookieNames.AccessToken, out var accessToken);

        if (!string.IsNullOrWhiteSpace(accessToken)
            && TryBuildUserFromAccessToken(accessToken!, out var user, out var atExpires)
            && IsAccessTokenStillValid(atExpires))
        {
            return Ok(new AuthCookieSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiry = atExpires,
                RefreshTokenExpiry = null,
                CurrentUser = user
            });
        }

        var login = await TryRefreshAsync(refreshToken!);
        if (login is null || !login.Success)
        {
            Response.ClearAuthCookies();
            return NoContent();
        }

        Response.AppendAuthCookies(login, Request.IsHttps);

        if (!TryBuildUserFromAccessToken(login.AccessToken, out var userAfterRefresh, out var newAtExp)
            || !IsAccessTokenStillValid(newAtExp))
        {
            Response.ClearAuthCookies();
            return NoContent();
        }

        var profile = await TryFetchUserProfileAsync(userAfterRefresh.UserID, login.AccessToken);
        var currentUser = profile ?? userAfterRefresh;

        return Ok(new AuthCookieSession
        {
            AccessToken = login.AccessToken,
            RefreshToken = login.RefreshToken,
            AccessTokenExpiry = newAtExp,
            RefreshTokenExpiry = login.RTExpiresAt,
            CurrentUser = currentUser
        });
    }

    [HttpPost("store")]
    public IActionResult Store([FromBody] AuthCookieSession state)
    {
        if (string.IsNullOrWhiteSpace(state.AccessToken) || string.IsNullOrWhiteSpace(state.RefreshToken))
        {
            return BadRequest("Missing access or refresh token.");
        }

        if (!state.AccessTokenExpiry.HasValue || !state.RefreshTokenExpiry.HasValue)
        {
            return BadRequest("Missing token expiry.");
        }

        Response.AppendAuthCookies(
            state.AccessToken,
            state.RefreshToken,
            state.AccessTokenExpiry.Value,
            state.RefreshTokenExpiry.Value,
            Request.IsHttps);

        return Ok();
    }

    [HttpPost("clear")]
    public IActionResult Clear()
    {
        Response.ClearAuthCookies();
        return Ok();
    }

    private static bool IsAccessTokenStillValid(DateTimeOffset accessExpiresUtc)
    {
        var now = DateTimeOffset.UtcNow.ToUniversalTime();
        return accessExpiresUtc + AccessTokenValidationClockSkew > now;
    }

    private async Task<LoginResponseDto?> TryRefreshAsync(string refreshToken)
    {
        try
        {
            var response = await _api.PostAsJsonAsync("/api/Auth/refresh", new RefreshTokenRequestDto
            {
                RefreshToken = refreshToken
            });

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        }
        catch
        {
            return null;
        }
    }

    private async Task<UserResponseDto?> TryFetchUserProfileAsync(int userId, string accessToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/User/{userId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var response = await _api.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<UserResponseDto>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads identity from the access JWT without validating the signature (browser already holds an API-issued token).
    /// Always use the <c>exp</c> claim as UTC unix time - do not rely on <see cref="JwtSecurityToken.ValidTo"/> alone for comparisons.
    /// </summary>
    private static bool TryBuildUserFromAccessToken(
        string token,
        out UserResponseDto user,
        out DateTimeOffset accessExpiresUtc)
    {
        user = null!;
        accessExpiresUtc = default;
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            var expClaim = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Exp)?.Value;
            if (string.IsNullOrEmpty(expClaim) || !long.TryParse(expClaim, out var expUnix))
            {
                return false;
            }

            accessExpiresUtc = DateTimeOffset.FromUnixTimeSeconds(expUnix);

            var uidClaim = jwt.Claims.FirstOrDefault(c => c.Type == "userId")?.Value;
            if (string.IsNullOrEmpty(uidClaim) || !int.TryParse(uidClaim, out var userId))
            {
                return false;
            }

            var email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value ?? string.Empty;
            var userName = jwt.Claims.FirstOrDefault(c => c.Type == "userName")?.Value ?? string.Empty;
            var role = jwt.Claims.FirstOrDefault(c => c.Type == "user role")?.Value ?? "Player";

            user = new UserResponseDto
            {
                UserID = userId,
                Email = email,
                UserName = userName,
                IsAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase),
                IsBanned = false,
                IsActive = true
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
}