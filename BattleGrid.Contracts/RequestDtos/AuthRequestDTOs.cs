using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BattleGrid.Contracts.RequestDtos
{
    public class RegisterRequestDto
    {
        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        [Required]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class LoginRequestDto
    {

        // Users will not see and use UserID. We may use it with internal services
        // So, it is nullable and hidden from users while they are filling forms
        // And we may pass it to controller via route and put it in here to use in internal systems
        [JsonIgnore]
        public int? UserID { get; set; }

        // It can be email or username. So, named it as LoginInfo
        [Required]
        public string LoginInfo { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class PasswordUpdateRequestDto
    {
        // It is required but ofcourse, we will not ask the user for their ID.
        // We will take it from the access token cookie and place it in the DTO
        [Required]
        [JsonIgnore]
        public int UserID { get; set; }

        [Required]
        public string OldPassword { get; set; } = string.Empty;

        [Required]
        public string NewPassword { get; set; } = string.Empty;

        [Required]
        public string ConfirmNewPassword { get; set; } = string.Empty;
    }

    /* In case there is an expired RefreshToken in user's browser cookies,
     *  we will use the expired token to determine the user and request a new RefreshToken
     */
    public class RefreshTokenRequestDto
    {
        public string? RefreshToken { get; set; }
    }
}