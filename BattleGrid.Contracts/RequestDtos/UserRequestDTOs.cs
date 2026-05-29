using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BattleGrid.Contracts.RequestDtos
{
    public class ChangeEmailRequestDto
    {
        [Required]
        public string NewEmail { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class ChangeUsernameRequestDto
    {
        [Required]
        public string NewUserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class DeactivateAccountRequestDto
    {
        [Required]
        public string Password { get; set; } = string.Empty;
    }
}