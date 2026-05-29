using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;


namespace BattleGrid.Application.Services
{

    public class AdminServices : IAdminServices
    {
        private readonly BattleGridDbContext _context;
        private readonly IUserServices _userServices;
        private readonly INormalizationHelper _normalizationHelper;

        public AdminServices(BattleGridDbContext context, 
                             IUserServices userServices,
                             INormalizationHelper normalizationHelper)
        {
            _context = context;
            _userServices = userServices;
            _normalizationHelper = normalizationHelper;
        }

        // NOTE: This service assumes User.IsBanned and BanList entries are in sync and up-to-date!
        public async Task<GeneralResponseDto> BanPlayerAsync(BanRequestDto dto)
        {
            // Create the response with default and/or null values
            var response = new GeneralResponseDto
            {
                Success = false,        // False by default
                Message = string.Empty  // Empty. It will be filled based on where do we return
            };

            var admin = await _userServices.GetByIdAsync(dto.AdminID);

            // Check if the AdminID is valid and belongs to an actual admin
            if (admin == null || !admin.IsAdmin)
            {
                response.Message = "You do NOT have permissions to complete this action!";
                return response;
            }

            // Normalize the username or email
            dto.PlayerInfo = await _normalizationHelper.NormalizeLoginInfoAsync(dto.PlayerInfo);

            // We get the user with the DbContext instead of UserServices because we may update its IsBanned and IsActive fields.
            var user = await _context.User
                .Where(u => u.Email == dto.PlayerInfo
                         || u.UserName == dto.PlayerInfo)
                .FirstOrDefaultAsync();

            // Check if the Player info is valid
            if (user == null)
            {
                response.Message = "There is no such user. You can not ban someone if they do not exist!";
                return response;
            }

            if (user.UserID == admin.UserID)
            {
                response.Message = "Why are you trying to ban yourself?";
                return response;
            }

            // From this point forward, we issue a ban or update an existing ban. Return result should be success
            response.Success = true;

            // If the player is already marked as banned
            if (user.IsBanned)
            {
                // Get the latest ban info on the user
                var latestBan = await _context.BanList
                    .Where(b => b.PlayerID == user.UserID)
                    .OrderByDescending(b => b.BanID)
                    .FirstOrDefaultAsync();

                if (latestBan != null) // We won't need this check as soon as we make sure User.IsBanned and BanList tables are in sync, via other means
                {

                    // If the current ban is permanent and it is not reverted, we do not need to do anything. Just return
                    if (!latestBan.IsTemporary && !latestBan.IsReverted) // Second condition won't be necessary after we make sure User.IsBanned and BanList tables are in sync
                    {
                        response.Message = "This user is already permanently banned. No further action is required.";
                        return response;
                    }

                    // If the current ban is temporary but the new ban request is for a permanent ban, 
                    //  create a new BanList entry for the new permanent ban. Old, temporary one can stay as is.
                    // Does not matter if the current ban's duration has ended or not, we are gonna create a new permanent record
                    if (latestBan.IsTemporary && !dto.IsTemporary)
                    {
                        var newBan = new BanList
                        {
                            AdminID = dto.AdminID,
                            PlayerID = user.UserID,
                            BanReason = dto.BanReason,
                            IsTemporary = dto.IsTemporary,
                            BannedAt = dto.BannedAt,
                            Duration = dto.Duration,
                            BannedUntil = ResolveBannedUntil(dto)
                        };

                        // Save changes to the DB
                        await _context.BanList.AddAsync(newBan);
                        await _context.SaveChangesAsync();

                        response.Message = "Previously temporarily banned player is now permanently banned!";
                        return response;
                    }

                    if (latestBan.IsTemporary && !latestBan.IsReverted && dto.IsTemporary)
                    {
                        // Increase the current ban's duration if the new ban request is longer             
                        if (latestBan.Duration < dto.Duration)
                        {
                            latestBan.Duration = dto.Duration;
                            latestBan.BannedUntil = ResolveBannedUntil(dto, latestBan.BannedAt);
                            await _context.SaveChangesAsync();

                            response.Message = "Ban duration is increased.";
                            return response;
                        }

                        response.Message = "Current ban has a longer duration. No action is taken!";
                        return response;
                    }
                }
            }

            // Player is not currently banned. Create a new BanList entry
            //aaa TODO: Remove the 'if (latestBan.IsTemporary && !dto.IsTemporary)' section. Since, it can simply be handled here
            //aaa Player is temporarily banned, we are issuing a permanent ban. Create a new BanList entry
            
            var banEntry = new BanList
            {
                AdminID = dto.AdminID,
                PlayerID = user.UserID,
                BanReason = dto.BanReason,
                IsTemporary = dto.IsTemporary,
                BannedAt = dto.BannedAt,
                Duration = dto.Duration,
                BannedUntil = ResolveBannedUntil(dto)
            };

            // Update the User table entry, as well
            user.IsBanned = true;
            user.LastUpdatedAt = dto.BannedAt;
            string banType = (dto.IsTemporary) ? "temporarily" : "permanently";
            user.UpdateReason = $"User is banned {banType}";

            // Save changes to the DB
            await _context.BanList.AddAsync(banEntry);
            await _context.SaveChangesAsync();

            response.Message = $"Player {dto.PlayerInfo} is banned for {dto.Duration} days.";
            return response;
        }

        public async Task<GeneralResponseDto> UnbanPlayerAsync(UnbanPlayerRequestDto dto)
        {
            var response = new GeneralResponseDto { Success = false, Message = string.Empty };

            var admin = await _userServices.GetByIdAsync(dto.AdminID);
            if (admin is null || !admin.IsAdmin)
            {
                response.Message = "You do NOT have permissions to complete this action!";
                return response;
            }

            dto.PlayerInfo = await _normalizationHelper.NormalizeLoginInfoAsync(dto.PlayerInfo);
            var reason = dto.Reason.Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                response.Message = "A reason is required to unban a player.";
                return response;
            }

            var user = await _context.User
                .Where(u => u.Email == dto.PlayerInfo || u.UserName == dto.PlayerInfo)
                .FirstOrDefaultAsync();

            if (user is null)
            {
                response.Message = "There is no such user.";
                return response;
            }

            var latestActiveBan = await _context.BanList
                .Where(b => b.PlayerID == user.UserID && !b.IsReverted)
                .OrderByDescending(b => b.BanID)
                .FirstOrDefaultAsync();

            if (latestActiveBan is null && !user.IsBanned)
            {
                response.Message = "This player is not currently banned.";
                return response;
            }

            var now = DateTimeOffset.UtcNow.ToUniversalTime();

            if (latestActiveBan is not null)
            {
                latestActiveBan.IsReverted = true;
                latestActiveBan.RevertingAdminID = dto.AdminID;
                latestActiveBan.RevertingReason = reason;
            }

            user.IsBanned = false;
            user.LastUpdatedAt = now;
            user.UpdateReason = $"Ban reverted by administrator (admin #{dto.AdminID}).";

            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message = latestActiveBan is null
                ? $"Player {dto.PlayerInfo} is unbanned (user flag cleared; no active ban row was found)."
                : $"Player {dto.PlayerInfo} has been unbanned. Ban #{latestActiveBan.BanID} was reverted.";
            return response;
        }

        public async Task<GeneralResponseDto> GrantAdminAsync(GrantAdminRequestDto dto)
        {
            var response = new GeneralResponseDto { Success = false, Message = string.Empty };

            var admin = await _userServices.GetByIdAsync(dto.AdminID);
            if (admin is null || !admin.IsAdmin)
            {
                response.Message = "You do NOT have permissions to complete this action!";
                return response;
            }

            dto.PlayerInfo = await _normalizationHelper.NormalizeLoginInfoAsync(dto.PlayerInfo);

            var user = await _context.User
                .Where(u => u.Email == dto.PlayerInfo || u.UserName == dto.PlayerInfo)
                .FirstOrDefaultAsync();

            if (user is null)
            {
                response.Message = "There is no such user.";
                return response;
            }

            if (user.UserID == admin.UserID)
            {
                response.Message = "You already have administrator privileges.";
                return response;
            }

            if (user.IsAdmin)
            {
                response.Message = "This player is already an administrator.";
                return response;
            }

            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            user.IsAdmin = true;
            user.LastUpdatedAt = now;
            user.UpdateReason = $"Granted admin by administrator (admin #{dto.AdminID}).";
            await _context.SaveChangesAsync();

            response.Success = true;
            response.Message =
                $"Player {dto.PlayerInfo} is now an administrator. They must sign in again for admin access to take effect.";
            return response;
        }

        private static DateTimeOffset? ResolveBannedUntil(BanRequestDto dto, DateTimeOffset? bannedAtOverride = null)
        {
            if (dto.BannedUntil.HasValue)
                return dto.BannedUntil.Value.ToUniversalTime();

            if (!dto.IsTemporary || !dto.Duration.HasValue)
                return null;

            var bannedAt = (bannedAtOverride ?? dto.BannedAt).ToUniversalTime();
            return bannedAt.Add(dto.Duration.Value);
        }
    }
}