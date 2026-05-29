
using BattleGrid.API.Extensions;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BattleGrid.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthServices _authServices;

        public AuthController(IAuthServices authServices)
        {
            _authServices = authServices;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
        {
            try
            {
                var result = await _authServices.RegisterAsync(dto);
                if (!result.Success)
                {
                    return BadRequest($"An error occured while registering the user: {result.Message}");
                }

                return Ok(result.Message);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Registration error: {ex.Message}");
            }
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
        {
            try
            {
                var loginResponse = await _authServices.LoginAsync(dto);

                if (loginResponse == null)
                {
                    return Unauthorized("Incorrect username/email or password.");
                }

                if (!loginResponse.Success)
                {
                    return Unauthorized(loginResponse.Message);
                }

                Response.AppendAuthCookies(loginResponse, Request.IsHttps);
                return Ok(loginResponse);
            }
            catch
            {
                return StatusCode(500, "An error occured during login.");
            }

        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenRequestDto? dto)
        {
            try
            {
                var refreshToken = Request.ResolveRefreshToken(dto?.RefreshToken);
                var result = await _authServices.LogoutAsync(refreshToken ?? string.Empty);

                Response.ClearAuthCookies();

                if (!result.Success)
                {
                    return BadRequest(result.Message);
                }

                return Ok(result.Message);
            }
            catch
            {
                return StatusCode(500, "An error occured during logout.");
            }
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto? dto)
        {
            try
            {
                var refreshToken = Request.ResolveRefreshToken(dto?.RefreshToken);
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    return Unauthorized("Refresh token is required.");
                }

                var refreshResponse = await _authServices.RefreshAccessTokenAsync(refreshToken);
                if (!refreshResponse.Success)
                {
                    Response.ClearAuthCookies();
                    return Unauthorized(refreshResponse.Message);
                }

                Response.AppendAuthCookies(refreshResponse, Request.IsHttps);
                return Ok(refreshResponse);
            }
            catch
            {
                return StatusCode(500, "An error occured while refreshing the access token.");
            }
        }

        [Authorize]
        [HttpPatch("password")]
        public async Task<IActionResult> UpdatePassword([FromBody] PasswordUpdateRequestDto dto)
        {
            if (!User.TryGetAuthenticatedUserId(out var uid))
                return Unauthorized();

            try
            {
                dto.UserID = uid;

                var result = await _authServices.UpdatePasswordAsync(dto);
                if (!result.Success)
                {
                    return BadRequest(result.Message);
                }

                return Ok(new { result.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occured while updating password: {ex.Message}");
            }
        }
    }
}
