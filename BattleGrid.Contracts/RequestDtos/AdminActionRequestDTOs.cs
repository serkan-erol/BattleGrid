using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BattleGrid.Contracts.RequestDtos
{
    public class BanRequestDto
    {
        [JsonIgnore]
        [Required]
        public int AdminID { get; set; }

        //aaa We may or may not need UserID directly. We will uncomment this if the need arises
        // public int UserID { get; set; }

        // Email or UserName of the user to be banned
        [Required]
        public string PlayerInfo { get; set; } = string.Empty;

        [Required]
        public string BanReason { get; set; } = string.Empty;

        [Required]
        public bool IsTemporary { get; set; } = false;  // Permanent ban by default

        public DateTimeOffset BannedAt { get; set; } = DateTimeOffset.UtcNow.ToUniversalTime();

        public TimeSpan? Duration { get; set; } = TimeSpan.FromDays(36500); // 100 years by default for permanent ban

        public DateTimeOffset? BannedUntil { get; set; }
    }

    public class UnbanPlayerRequestDto
    {
        [JsonIgnore]
        [Required]
        public int AdminID { get; set; }

        [Required]
        public string PlayerInfo { get; set; } = string.Empty;

        [Required]
        public string Reason { get; set; } = string.Empty;
    }

    public class GrantAdminRequestDto
    {
        [JsonIgnore]
        [Required]
        public int AdminID { get; set; }

        [Required]
        public string PlayerInfo { get; set; } = string.Empty;
    }
}