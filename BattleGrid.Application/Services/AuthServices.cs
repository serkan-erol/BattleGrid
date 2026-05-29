using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;


namespace BattleGrid.Application.Services
{
    public class AuthServices : IAuthServices
    {
        private static readonly TimeSpan ProfileFieldChangeCooldown = TimeSpan.FromDays(30);

        private readonly BattleGridDbContext _context;
        private readonly IUserServices _userServices;
        private readonly IJwtHelper _jwtHelper;
        private readonly INormalizationHelper _normalizationHelper;
        private readonly IPlayerStatSeasonService _playerStatSeason;
        private readonly IBanListServices _banListServices;

        public AuthServices(BattleGridDbContext context, 
                            IUserServices userServices,
                            IJwtHelper jwtHelper,
                            INormalizationHelper normalizationHelper,
                            IPlayerStatSeasonService playerStatSeason,
                            IBanListServices banListServices)
        {
            _context = context;
            _userServices = userServices;
            _jwtHelper = jwtHelper;
            _normalizationHelper = normalizationHelper;
            _playerStatSeason = playerStatSeason;
            _banListServices = banListServices;
        }

        public async Task<GeneralResponseDto> RegisterAsync(RegisterRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.UserName))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "User name is required."
                };
            }

            dto.UserName = await _normalizationHelper.NormalizeLoginInfoAsync(dto.UserName);
            if (dto.UserName.Contains('@', StringComparison.Ordinal))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "User name cannot contain '@' (use the email field for email)."
                };
            }

            if (!Regex.IsMatch(dto.UserName, @"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "User name may only contain letters, numbers, and underscores."
                };
            }

            if (!string.Equals(dto.Password, dto.ConfirmPassword, StringComparison.Ordinal))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "Password and confirmation do not match."
                };
            }

            // Check if a user with the same email already exists
            // Make sure email is in a proper format (trimmed and lowercase) before checking for existing users
            dto.Email = await _normalizationHelper.NormalizeLoginInfoAsync(dto.Email);

            var exists = await _context.User
                /* NOTE: We won't make any changes to this user entity in this service.
                 * We only need to read the user info and verify the email does not already exist in our DB.
                 * So, add no tracking (AsNoTracking()) to improve performance.
                 */
                .AsNoTracking()
                .AnyAsync(x => x.Email == dto.Email);

            if (exists)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "This email is already registered."
                };
            }

            var userNameExists = await _context.User
                .AsNoTracking()
                .AnyAsync(x => x.UserName == dto.UserName);

            if (userNameExists)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "This user name is taken."
                };
            }

            // Create new user
            var newUser = new User
            {
                UserName = dto.UserName,
                Email = dto.Email,                                              // Save the properly formatted email
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)     // and the hashed password
                // The rest takes the default values
            };

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.User.AddAsync(newUser);
                await _context.SaveChangesAsync();

                await _playerStatSeason.EnsureMatchmakingRatingAsync(newUser.UserID);

                await tx.CommitAsync();

                return new GeneralResponseDto
                {
                    Success = true,
                    Message = "User registered succesfully."
                };
            }
            catch (Exception)
            {
                await tx.RollbackAsync();
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "An error occured while saving user data!"
                };
            }
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto dto)
        {
            dto.LoginInfo = await _normalizationHelper.NormalizeLoginInfoAsync(dto.LoginInfo);

            // Get the user info
            var user = await _userServices.GetByLoginInfoAsync(dto.LoginInfo);

            if (user == null)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "User does not exist",
                    AccessToken = string.Empty,
                    RefreshToken = string.Empty,
                    ATExpiresAt = DateTimeOffset.MinValue,
                    RTExpiresAt = DateTimeOffset.MinValue
                };
            }

            // Deleted account (or maybe permanently banned account)
            if (!user.IsActive)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Account is deactivated (deleted by the user or the system)",
                    AccessToken = string.Empty,
                    RefreshToken = string.Empty,
                    ATExpiresAt = DateTimeOffset.MinValue,
                    RTExpiresAt = DateTimeOffset.MinValue
                };
            }

            if (user.IsBanned)
            {
                await _banListServices.RefreshPlayerBanStateAsync(user.UserID);
                user = await _userServices.GetByLoginInfoAsync(dto.LoginInfo);

                if (user is null)
                    return new LoginResponseDto
                    {
                        Success = false,
                        Message = "User does not exist",
                        AccessToken = string.Empty,
                        RefreshToken = string.Empty,
                        ATExpiresAt = DateTimeOffset.MinValue,
                        RTExpiresAt = DateTimeOffset.MinValue
                    };

                if (user.IsBanned)
                {
                    var ban = await _context.BanList
                        .AsNoTracking()
                        .Where(b => b.PlayerID == user.UserID)
                        .OrderByDescending(b => b.BanID)
                        .FirstOrDefaultAsync();

                    var detail = FormatBanMessageDetail(ban);

                    return new LoginResponseDto
                    {
                        Success = false,
                        Message = $"You are banned {detail}",
                        AccessToken = string.Empty,
                        RefreshToken = string.Empty,
                        ATExpiresAt = DateTimeOffset.MinValue,
                        RTExpiresAt = DateTimeOffset.MinValue
                    };
                }
            }

            // User is not banned and account is active
            // Not bool but var because VerifyPasswordAsync returns GeneralResponseDto with bool success and string message fields
            var authSuccess = await VerifyPasswordAsync(dto);

            if (!authSuccess.Success)
            {
                return new LoginResponseDto
                {
                    Success = authSuccess.Success,
                    Message = authSuccess.Message,
                    AccessToken = string.Empty,
                    RefreshToken = string.Empty,
                    ATExpiresAt = DateTimeOffset.MinValue,
                    RTExpiresAt = DateTimeOffset.MinValue
                };
            }

            /* NOTE: Since we might update the session entity, we will not use AsNoTracking() here!
             * We will have only one active session per user.
             * If there is already an active session for the user, 
             * we will update the session with new access and refresh tokens.
             */
            var existingSession = await _context.Session
                .Where(s => s.UserID == user.UserID)
                .FirstOrDefaultAsync();

            user.Email = await _normalizationHelper.NormalizeLoginInfoAsync(user.Email);

            string accessToken = await _jwtHelper.GenerateAccessTokenAsync(user.Email);
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentNullException(nameof(accessToken), "Access token is empty");
            }

            DateTimeOffset accessTokenExpiration = _jwtHelper.GetAccessTokenExpiration();
            var now = DateTimeOffset.UtcNow.ToUniversalTime();

            string refreshToken;
            DateTimeOffset refreshTokenExpiration;

            /* Lost browser cookies but DB may still hold a valid, non-revoked refresh - reuse it
             * instead of minting a new one (standard practice to avoid churn and orphan refresh rows).
             */
            bool reuseExistingRefresh = existingSession is not null
                && !existingSession.IsRevoked
                && existingSession.RT_ExpiresAt > now;

            if (reuseExistingRefresh)
            {
                refreshToken = existingSession!.RefreshToken;
                refreshTokenExpiration = existingSession.RT_ExpiresAt;
            }
            else
            {
                refreshToken = await _jwtHelper.GenerateRefreshTokenAsync();
                if (string.IsNullOrEmpty(refreshToken))
                {
                    throw new ArgumentNullException(nameof(refreshToken), "Refresh token is empty");
                }

                refreshTokenExpiration = _jwtHelper.GetRefreshTokenExpiration();
            }

            /* If there is currently a session for this user, do not create a new one.
             * Update the existing one with new access token; refresh only if DB row cannot be reused.
             * ** LastLogin info is updated by the DB itself
             */
            if (existingSession != null)
            {
                existingSession.AccessToken = accessToken;
                existingSession.AT_ExpiresAt = accessTokenExpiration;
                if (!reuseExistingRefresh)
                {
                    existingSession.RefreshToken = refreshToken;
                    existingSession.RT_ExpiresAt = refreshTokenExpiration;
                }

                existingSession.IsRevoked = false;
                existingSession.LastUpdatedAt = now;

                await _context.SaveChangesAsync();

                // Finish login process by returning the tokens and expiration dates.
                return new LoginResponseDto
                {
                    Success = true,
                    Message = "Login successful.",
                    AccessToken = accessToken,
                    RefreshToken = refreshToken,
                    ATExpiresAt = accessTokenExpiration,
                    RTExpiresAt = refreshTokenExpiration
                };
            }

            // When there is no session for the user, create one.
            var session = new Session
            {
                UserID = user.UserID,
                AccessToken = accessToken,
                AT_ExpiresAt = accessTokenExpiration,
                RefreshToken = refreshToken,
                RT_ExpiresAt = refreshTokenExpiration,
                LastLogin = DateTimeOffset.UtcNow.ToUniversalTime(),
                IsRevoked = false
            };

            await _context.Session.AddAsync(session);
            await _context.SaveChangesAsync();

            return new LoginResponseDto
            {
                Success = true,
                Message = "Login successful.",
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ATExpiresAt = accessTokenExpiration,
                RTExpiresAt = refreshTokenExpiration
            };
        }

        public async Task<GeneralResponseDto> LogoutAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "Refresh token is required."
                };
            }

            var session = await _context.Session
                .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);

            if (session is null)
            {
                return new GeneralResponseDto
                {
                    Success = true,
                    Message = "Logged out."
                };
            }

            session.IsRevoked = true;
            session.LastUpdatedAt = DateTimeOffset.UtcNow.ToUniversalTime();
            await _context.SaveChangesAsync();

            return new GeneralResponseDto
            {
                Success = true,
                Message = "Logged out."
            };
        }

        public async Task<LoginResponseDto> RefreshAccessTokenAsync(string refreshToken)
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Refresh token is required."
                };
            }

            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            var session = await _context.Session
                .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken);

            if (session is null)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Invalid refresh token."
                };
            }

            if (session.IsRevoked)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Session has been revoked."
                };
            }

            if (session.RT_ExpiresAt <= now)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Refresh token has expired."
                };
            }

            var user = await _context.User
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserID == session.UserID);

            if (user is null || !user.IsActive)
            {
                return new LoginResponseDto
                {
                    Success = false,
                    Message = "Account is not available."
                };
            }

            if (user.IsBanned)
            {
                if (!await _banListServices.RefreshPlayerBanStateAsync(user.UserID))
                {
                    return new LoginResponseDto
                    {
                        Success = false,
                        Message = "Account is banned."
                    };
                }

                user = await _context.User
                    .AsNoTracking()
                    .FirstAsync(u => u.UserID == session.UserID);
            }

            var email = await _normalizationHelper.NormalizeLoginInfoAsync(user.Email);
            var accessToken = await _jwtHelper.GenerateAccessTokenAsync(email);
            var accessTokenExpiration = _jwtHelper.GetAccessTokenExpiration();

            session.AccessToken = accessToken;
            session.AT_ExpiresAt = accessTokenExpiration;
            session.LastUpdatedAt = now;
            await _context.SaveChangesAsync();

            return new LoginResponseDto
            {
                Success = true,
                Message = "Access token refreshed.",
                AccessToken = accessToken,
                RefreshToken = session.RefreshToken,
                ATExpiresAt = accessTokenExpiration,
                RTExpiresAt = session.RT_ExpiresAt
            };
        }

        public async Task<GeneralResponseDto> VerifyPasswordAsync(LoginRequestDto dto)
        {
            dto.LoginInfo = await _normalizationHelper.NormalizeLoginInfoAsync(dto.LoginInfo);
            
            // Get the user info for the user with the matching email or username (or ID for background service uses)
            /* NOTE: Did not use userServices.GetByLoginInfoAsync because it returns UserResponseDto 
             * and this DTO does not include user's PasswordHash
             * Instead, called the entire user info with EF Core context directly
             */
            var user = await _context.User
                .Where(u => u.UserName == dto.LoginInfo
                         || u.Email == dto.LoginInfo
                         || u.UserID == dto.UserID)
                .AsNoTracking() 
                .FirstOrDefaultAsync();
            
            if (user == null)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "User not found!"
                };
            }

            // Hash the password for initial test users that were inserted via SQL
            // They have plain txt passwords in DB, replace it
            var passwordHash = "";
            if (!user.PasswordHash.StartsWith("$2a$")) // $2a$ is the standart first 4 characters for BCrypt hashes
            {
                passwordHash = await _userServices.HashPasswordAsync(user.UserID);
            }
            else
            {
                passwordHash = user.PasswordHash;
            }

            // Check if the passwords match
            bool passwordsMatch = BCrypt.Net.BCrypt.Verify(dto.Password, passwordHash);

            if (!passwordsMatch)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "Invalid username or password!"
                };
            }

            return new GeneralResponseDto
            {
                Success = true,
                Message = "Password accepted."
            };
        }

        public async Task<GeneralResponseDto> UpdatePasswordAsync(PasswordUpdateRequestDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "New password is required."
                };
            }
                        
            if (string.IsNullOrWhiteSpace(dto.ConfirmNewPassword))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "Password confirmation is required."
                };
            }

            if (!string.Equals(dto.NewPassword, dto.ConfirmNewPassword, StringComparison.Ordinal))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "New password and confirmation do not match."
                };
            }

            var user = await _context.User
                .FirstOrDefaultAsync(u => u.UserID == dto.UserID);

            if (user is null)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "User not found!"
                };
            }

            if (!user.IsActive)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "This account is deactivated."
                };
            }

            var verificationDto = new LoginRequestDto
            {
                UserID = dto.UserID,
                LoginInfo = user.Email,
                Password = dto.OldPassword
            };

            // Verify the old password. Also verifies the user-password match
            var authSuccess = await VerifyPasswordAsync(verificationDto);

            /* If user is not authenticated return the output of VerifyPasswordAsync directly
             * Since they both return GeneralResponseDto,
             * and VerifyPasswordAsync already returns the appropriate response
             */
            if (!authSuccess.Success)
            {
                return authSuccess;
            }

            // If new password matches with old password, there is nothing to do. Just return failure message
            // This only compares old and new password fields in the DTO. Does NOT compare against user's actual password hash
            // However, we are doing this check after the verification. So, old password field has to be correct!
            if (string.Equals(dto.OldPassword, dto.NewPassword, StringComparison.Ordinal))
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "New password can not be the same as old password."
                };
            }

            // Hash the new password
            var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

            // Update the PasswordHash and the LastUpdatedAt in the database
            user.PasswordHash = newPasswordHash;
            user.LastUpdatedAt = DateTimeOffset.UtcNow.ToUniversalTime();
            user.UpdateReason = "Password changed by user";

            // Save the changes to the database
            await _context.SaveChangesAsync();

            // Verify the new password
            var verifySuccess = BCrypt.Net.BCrypt.Verify(dto.NewPassword, user.PasswordHash);

            if (!verifySuccess)
            {
                return new GeneralResponseDto
                {
                    Success = false,
                    Message = "Something went wrong while updating the password!"
                };
            }

            return new GeneralResponseDto
            {
                Success = true,
                Message = "Password updated successfuly."
            };
        }

        public async Task<(GeneralResponseDto Result, UserResponseDto? UpdatedUser)> ChangeEmailAsync(
            int userId,
            ChangeEmailRequestDto dto,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dto.NewEmail))
                return (Fail("New email is required."), null);

            var newEmail = await _normalizationHelper.NormalizeLoginInfoAsync(dto.NewEmail);

            var user = await _context.User.FirstOrDefaultAsync(u => u.UserID == userId, cancellationToken);
            if (user is null)
                return (Fail("User not found."), null);

            if (!user.IsActive)
                return (Fail("This account is deactivated."), null);

            if (string.Equals(user.Email, newEmail, StringComparison.Ordinal))
                return (Fail("That is already your email address."), null);

            var passwordCheck = await VerifyPasswordForUserAsync(user, dto.Password);
            if (!passwordCheck.Success)
                return (passwordCheck, null);

            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            if (IsWithinMonthlyCooldown(user.LastEmailChangeAt, now))
            {
                return (Fail(
                    $"Email can only be changed once per month. {FormatNextChangeAvailable(user.LastEmailChangeAt!.Value)}"),
                    null);
            }

            var emailTaken = await _context.User
                .AsNoTracking()
                .AnyAsync(u => u.Email == newEmail && u.UserID != userId, cancellationToken);

            if (emailTaken)
                return (Fail("This email is already registered."), null);

            user.Email = newEmail;
            user.LastUpdatedAt = now;
            user.LastEmailChangeAt = now;
            user.UpdateReason = "Email changed by user (settings)";

            await _context.SaveChangesAsync(cancellationToken);

            return (Ok("Email updated successfully."), MapUserToResponse(user));
        }

        public async Task<(GeneralResponseDto Result, UserResponseDto? UpdatedUser)> ChangeUsernameAsync(
            int userId,
            ChangeUsernameRequestDto dto,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dto.NewUserName))
                return (Fail("User name is required."), null);

            var newUserName = await _normalizationHelper.NormalizeLoginInfoAsync(dto.NewUserName);

            if (newUserName.Contains('@', StringComparison.Ordinal))
                return (Fail("User name cannot contain '@' (use the email field for email)."), null);

            if (!Regex.IsMatch(newUserName, @"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant))
                return (Fail("User name may only contain letters, numbers, and underscores."), null);

            var user = await _context.User.FirstOrDefaultAsync(u => u.UserID == userId, cancellationToken);
            if (user is null)
                return (Fail("User not found."), null);

            if (!user.IsActive)
                return (Fail("This account is deactivated."), null);

            if (string.Equals(user.UserName, newUserName, StringComparison.Ordinal))
                return (Fail("That is already your user name."), null);

            var passwordCheck = await VerifyPasswordForUserAsync(user, dto.Password);
            if (!passwordCheck.Success)
                return (passwordCheck, null);

            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            if (IsWithinMonthlyCooldown(user.LastUserNameChangeAt, now))
            {
                return (Fail(
                    $"User name can only be changed once per month. {FormatNextChangeAvailable(user.LastUserNameChangeAt!.Value)}"),
                    null);
            }

            var nameTaken = await _context.User
                .AsNoTracking()
                .AnyAsync(u => u.UserName == newUserName && u.UserID != userId, cancellationToken);

            if (nameTaken)
                return (Fail("This user name is taken."), null);

            user.UserName = newUserName;
            user.LastUpdatedAt = now;
            user.LastUserNameChangeAt = now;
            user.UpdateReason = "User name changed by user (settings)";

            await _context.SaveChangesAsync(cancellationToken);

            return (Ok("User name updated successfully."), MapUserToResponse(user));
        }

        public async Task<GeneralResponseDto> DeactivateAccountAsync(
            int userId,
            DeactivateAccountRequestDto dto,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                return Fail("Password is required.");

            var user = await _context.User.FirstOrDefaultAsync(u => u.UserID == userId, cancellationToken);
            if (user is null)
                return Fail("User not found.");

            if (!user.IsActive)
                return Fail("This account is already deactivated.");

            var passwordCheck = await VerifyPasswordForUserAsync(user, dto.Password);
            if (!passwordCheck.Success)
                return passwordCheck;

            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            user.IsActive = false;
            user.LastUpdatedAt = now;
            user.UpdateReason = "Account deactivated by user (settings)";

            var sessions = await _context.Session
                .Where(s => s.UserID == userId && !s.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var session in sessions)
            {
                session.IsRevoked = true;
                session.LastUpdatedAt = now;
            }

            await _context.SaveChangesAsync(cancellationToken);

            return Ok("Account deleted successfully.");
        }

        private async Task<GeneralResponseDto> VerifyPasswordForUserAsync(User user, string password)
        {
            return await VerifyPasswordAsync(new LoginRequestDto
            {
                UserID = user.UserID,
                LoginInfo = user.Email,
                Password = password
            });
        }

        private static bool IsWithinMonthlyCooldown(DateTimeOffset? lastChangeUtc, DateTimeOffset nowUtc) =>
            lastChangeUtc is not null && lastChangeUtc.Value + ProfileFieldChangeCooldown > nowUtc;

        private static string FormatNextChangeAvailable(DateTimeOffset lastChangeUtc)
        {
            var next = lastChangeUtc.ToUniversalTime().Add(ProfileFieldChangeCooldown);
            return $"Next change available after {next:yyyy-MM-dd HH:mm} UTC.";
        }

        private static GeneralResponseDto Ok(string message) =>
            new() { Success = true, Message = message };

        private static GeneralResponseDto Fail(string message) =>
            new() { Success = false, Message = message };

        private static UserResponseDto MapUserToResponse(User user) =>
            new()
            {
                UserID = user.UserID,
                UserName = user.UserName,
                Email = user.Email,
                IsAdmin = user.IsAdmin,
                IsBanned = user.IsBanned,
                IsActive = user.IsActive
            };

        private static string FormatBanMessageDetail(BanList? ban)
        {
            if (ban is null || ban.IsReverted)
                return "(contact support)";

            if (!ban.IsTemporary)
                return "INDEFINITELY!";

            var end = BanListServices.GetBanEndUtc(ban);
            if (!end.HasValue)
                return "INDEFINITELY!";

            return $"until {end.Value:yyyy-MM-dd HH:mm:ss} UTC";
        }
    }
}