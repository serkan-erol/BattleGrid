using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BattleGrid.Application.Services
{
    public class UserServices : IUserServices
    {
        /// <summary>Global current season plus this many prior seasons (3 rows total).</summary>
        const int MaxSeasonsInAdminProfile = 3;

        private readonly BattleGridDbContext _context;
        private readonly INormalizationHelper _normalizationHelper;
        private readonly IPlayerStatSeasonService _playerStatSeason;
        private readonly IBanListServices _banListServices;

        public UserServices(
            BattleGridDbContext context,
            INormalizationHelper normalizationHelper,
            IPlayerStatSeasonService playerStatSeason,
            IBanListServices banListServices)
        {
            _context = context;
            _normalizationHelper = normalizationHelper;
            _playerStatSeason = playerStatSeason;
            _banListServices = banListServices;
        }

        public const int DefaultUsersPageSize = 20;
        public const int MaxUsersPageSize = 100;

        public async Task<PagedUsersResponseDto> GetUsersPageAsync(int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, MaxUsersPageSize);

            var query = _context.User.OrderBy(c => c.UserID);
            var totalCount = await query.CountAsync();

            var totalPages = totalCount == 0
                ? 0
                : (int)Math.Ceiling(totalCount / (double)pageSize);

            if (totalPages > 0 && page > totalPages)
                page = totalPages;

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => MapToResponseDto(c))
                .AsNoTracking()
                .ToListAsync();

            return new PagedUsersResponseDto
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            };
        }

        public async Task<UserResponseDto?> GetByIdAsync(int userId)
        {
            var user = await _context.User
                .Where(c => c.UserID == userId)
                .Select(c => MapToResponseDto(c))
                .AsNoTracking()
                .FirstOrDefaultAsync();
            return user;
        }

        // Find a user via UserName or Email
        public async Task<UserResponseDto?> GetByLoginInfoAsync(string loginInfo)
        {
            string normalizedLoginInfo = await _normalizationHelper.NormalizeLoginInfoAsync(loginInfo);

            var user = await _context.User
                .Where(c => c.UserName == normalizedLoginInfo || c.Email == normalizedLoginInfo)
                .Select(c => MapToResponseDto(c))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            return user;
        }

        public async Task<string> HashPasswordAsync(int userId)
        {
            var user = await _context.User
                .Where(c => c.UserID == userId)
                .FirstOrDefaultAsync()
                ?? throw new Exception("User not found!"); // Redundant, AuthServices makes a null user check before calling

            //Redundant check since, we only call this service when the password is plain text in DB
            if (!user.PasswordHash.StartsWith("$2a$"))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);
                user.LastUpdatedAt = DateTimeOffset.UtcNow.ToUniversalTime();
                user.UpdateReason = "Password hashed by the system";

                await _context.SaveChangesAsync();
            }

            // Get the same user again with updated password field
            var userNew = await _context.User
                .Where(c => c.UserID == userId)
                .AsNoTracking()
                .FirstOrDefaultAsync()
                ?? throw new Exception("User not found!"); // Redundant, after all the checks to see if the user is null

            // Check if the password is updated to a hashed version
            if (!userNew.PasswordHash.StartsWith("$2a$"))
            {
                throw new Exception("An error occured during password hash creation");
            }

            return userNew.PasswordHash;
        }

        public async Task<bool> IsAdminAsync(int userId)
        {
            var user = await GetByIdAsync(userId);
            return user?.IsAdmin ?? false;
        }

        public async Task<AdminUserProfileResponseDto?> GetAdminUserProfileAsync(
            int targetUserId,
            CancellationToken cancellationToken = default)
        {
            var user = await GetByIdAsync(targetUserId);
            if (user is null)
                return null;

            var globalCurrentSeasonNo = await _playerStatSeason.GetGlobalCurrentSeasonNoAsync(cancellationToken);
            var allSeasonStats = await _playerStatSeason.GetAllSeasonStatsForUserAsync(targetUserId, cancellationToken);
            var minSeasonNo = globalCurrentSeasonNo - (MaxSeasonsInAdminProfile - 1);
            var seasonStats = allSeasonStats
                .Where(s => s.SeasonNo >= minSeasonNo)
                .OrderByDescending(s => s.SeasonNo)
                .Take(MaxSeasonsInAdminProfile)
                .ToList();

            var banStatus = await _banListServices.GetBanStatusForPlayerAsync(targetUserId, cancellationToken);

            return new AdminUserProfileResponseDto
            {
                User = user,
                GlobalCurrentSeasonNo = globalCurrentSeasonNo,
                SeasonStats = seasonStats,
                BanStatus = banStatus
            };
        }

        private static UserResponseDto MapToResponseDto(User user)
        {
            return new UserResponseDto
            {
                UserID = user.UserID,
                UserName = user.UserName,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                IsBanned = user.IsBanned,
                IsActive = user.IsActive
            };
        }
    }
}