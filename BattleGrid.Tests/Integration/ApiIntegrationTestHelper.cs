using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Domain.Enums;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BattleGrid.Tests.Integration;

internal static class ApiIntegrationTestHelper
{
    private static readonly AsyncLocal<IntegrationTestDataTracker?> ActiveTracker = new();

    public const string TestEmailDomain = "@battlegrid.test";
    public const string RegisterPath = "/api/Auth/register";
    public const string LoginPath = "/api/Auth/login";
    public const string LogoutPath = "/api/Auth/logout";
    public const string RefreshPath = "/api/Auth/refresh";
    public const string ExpectedRegisterSuccessMessage = "User registered succesfully.";
    public const string DefaultTestPassword = "PerfTest_Pass123!";

    public static void BindTracker(IntegrationTestDataTracker? tracker) => ActiveTracker.Value = tracker;

    public static void TrackEmail(string email) => ActiveTracker.Value?.TrackEmail(email);

    public static void TrackSession(TestUserSession session) => ActiveTracker.Value?.TrackSession(session);

    public static void TrackMatch(int matchId) => ActiveTracker.Value?.TrackMatch(matchId);

    public static bool IsIntegrationTestEmail(string email) =>
        email.EndsWith(TestEmailDomain, StringComparison.OrdinalIgnoreCase);

    public static RegisterRequestDto CreateUniqueRegisterRequest(string userNamePrefix = "api")
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        return new RegisterRequestDto
        {
            UserName = $"{userNamePrefix}_{suffix}",
            Email = $"{userNamePrefix}_{suffix}{TestEmailDomain}",
            Password = DefaultTestPassword,
            ConfirmPassword = DefaultTestPassword
        };
    }

    /// <summary>Raw POST to Auth/register. Caller owns the response (use <c>using</c>).</summary>
    public static async Task<HttpResponseMessage> RegisterAsync(HttpClient client, RegisterRequestDto request) =>
        await client.PostAsJsonAsync(RegisterPath, request);

    /// <summary>POST register and parse AuthController plain-text body (OK message or BadRequest error text).</summary>
    public static async Task<(HttpStatusCode StatusCode, string Message)> RegisterAndReadAsync(
        HttpClient client,
        RegisterRequestDto request)
    {
        using var response = await RegisterAsync(client, request);
        var body = await response.Content.ReadAsStringAsync();
        var status = response.StatusCode;
        if (status == HttpStatusCode.OK)
            TrackEmail(request.Email);

        return (status, ParseRegisterResponseBody(body));
    }

    /// <summary>Register a user for test setup (login, user, match fixtures). Not for timing register itself.</summary>
    public static async Task RegisterUserForSetupAsync(HttpClient client, RegisterRequestDto request)
    {
        var (status, message) = await RegisterAndReadAsync(client, request);
        if (status != HttpStatusCode.OK)
            throw new InvalidOperationException($"Test setup register failed ({status}): {message}");
    }

    /// <summary>Raw POST to Auth/login. Caller owns the response (use <c>using</c>).</summary>
    public static async Task<HttpResponseMessage> LoginPostAsync(
        HttpClient client,
        string loginInfo,
        string password) =>
        await client.PostAsJsonAsync(LoginPath, new LoginRequestDto
        {
            LoginInfo = loginInfo,
            Password = password
        });

    /// <summary>POST login for test setup when tokens are needed. Returns null if login fails.</summary>
    public static async Task<LoginResponseDto?> LoginAsync(HttpClient client, string loginInfo, string password)
    {
        using var response = await LoginPostAsync(client, loginInfo, password);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<LoginResponseDto>();
    }

    public static void SetBearerToken(HttpClient client, string accessToken) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    public static void ClearBearerToken(HttpClient client) =>
        client.DefaultRequestHeaders.Authorization = null;

    public static HttpClient CreateAuthenticatedClient(BattleGridApiFactory factory, string accessToken)
    {
        var client = factory.CreateClient();
        SetBearerToken(client, accessToken);
        return client;
    }

    public static async Task<TestUserSession> CreateRegisteredUserAsync(HttpClient client)
    {
        var request = CreateUniqueRegisterRequest();
        await RegisterUserForSetupAsync(client, request);

        var profile = await client.GetFromJsonAsync<UserResponseDto>(
            $"/api/User/{Uri.EscapeDataString(request.Email)}");

        var session = new TestUserSession(request, profile);
        TrackSession(session);
        return session;
    }

    public static async Task<TestUserSession> CreateLoggedInUserAsync(
        HttpClient client,
        BattleGridApiFactory factory,
        bool asAdmin = false)
    {
        var session = await CreateRegisteredUserAsync(client);
        if (asAdmin)
            await PromoteToAdminAsync(factory, session.Request.Email);

        var tokens = await LoginAsync(client, session.Request.Email, session.Request.Password);
        if (tokens is not { Success: true })
            throw new InvalidOperationException("Failed to log in test user.");

        var profile = await client.GetFromJsonAsync<UserResponseDto>(
            $"/api/User/{Uri.EscapeDataString(session.Request.Email)}");

        session = session with { Tokens = tokens, Profile = profile };
        TrackSession(session);
        return session;
    }

    public static async Task PromoteToAdminAsync(BattleGridApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        var user = await db.User.FirstAsync(u => u.Email == email);
        user.IsAdmin = true;
        await db.SaveChangesAsync();
    }

    public const string AdvanceSeasonPath = "/api/Admin/season/advance";

    public static Task<HttpResponseMessage> PostAdvanceSeasonAsync(HttpClient client) =>
        client.PostAsync(AdvanceSeasonPath, null);

    public static async Task<int> GetNextGlobalSeasonNoAsync(BattleGridApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        return await db.PlayerStat.AnyAsync()
            ? await db.PlayerStat.MaxAsync(p => p.SeasonNo) + 1
            : 1;
    }

    public static async Task SeedCurrentSeasonLeaderboardAsync(
        BattleGridApiFactory factory,
        IReadOnlyList<(int UserId, int Rating)> entries,
        int? seasonNo = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();

        var season = seasonNo ?? await GetNextGlobalSeasonNoAsync(factory);

        foreach (var (userId, rating) in entries)
        {
            var existing = await db.PlayerStat
                .FirstOrDefaultAsync(p => p.UserID == userId && p.SeasonNo == season);
            if (existing is not null)
            {
                existing.Rating = rating;
                existing.HighestRating = Math.Max(existing.HighestRating, rating);
                continue;
            }

            db.PlayerStat.Add(new PlayerStat
            {
                UserID = userId,
                SeasonNo = season,
                Rating = rating,
                HighestRating = rating,
                MatchesPlayed = 1,
                MatchesWon = 0,
                WinRate = 0
            });
        }

        await db.SaveChangesAsync();
    }

    public static async Task DeletePlayerStatSeasonAsync(
        BattleGridApiFactory factory,
        int userId,
        int seasonNo)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        var row = await db.PlayerStat
            .FirstOrDefaultAsync(p => p.UserID == userId && p.SeasonNo == seasonNo);
        if (row is null)
            return;

        db.PlayerStat.Remove(row);
        await db.SaveChangesAsync();
    }

    public static async Task<int> CreateReplayableMatchFixtureAsync(
        BattleGridApiFactory factory,
        int player1Id,
        int player2Id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();

        var shipId = await db.ShipType
            .AsNoTracking()
            .Select(s => s.ShipID)
            .FirstAsync();

        var match = new Match
        {
            Player1ID = player1Id,
            Player2ID = player2Id,
            Status = MatchStatus.P1Won,
            TotalNoOfMoves = 1,
            FinishReason = "All opponent ships were destroyed.",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            FinishedAt = DateTimeOffset.UtcNow
        };

        await db.Match.AddAsync(match);
        await db.SaveChangesAsync();

        await db.ShipPlacement.AddRangeAsync(
            new ShipPlacement
            {
                MatchID = match.MatchID,
                PlayerID = player1Id,
                ShipID = shipId,
                StartX = 0,
                StartY = 0,
                IsVertical = false
            },
            new ShipPlacement
            {
                MatchID = match.MatchID,
                PlayerID = player2Id,
                ShipID = shipId,
                StartX = 1,
                StartY = 1,
                IsVertical = true
            });

        await db.MatchMove.AddAsync(new MatchMove
        {
            MatchID = match.MatchID,
            PlayerID = player1Id,
            MoveNumber = 1,
            HitX = 1,
            HitY = 1,
            IsHit = true
        });

        await db.SaveChangesAsync();
        TrackMatch(match.MatchID);
        return match.MatchID;
    }

    public static async Task<int> CreateResumableMatchFixtureAsync(
        BattleGridApiFactory factory,
        int userId,
        int opponentUserId,
        MatchStatus status = MatchStatus.InProgress)
    {
        if (status is not (MatchStatus.InProgress or MatchStatus.PlacingShips or MatchStatus.Loading))
            throw new ArgumentOutOfRangeException(nameof(status), "Resumable fixture must use a resumable match status.");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();

        var match = new Match
        {
            Player1ID = userId,
            Player2ID = opponentUserId,
            Status = status,
            TotalNoOfMoves = 0,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            FinishedAt = null
        };

        await db.Match.AddAsync(match);
        await db.SaveChangesAsync();
        TrackMatch(match.MatchID);
        return match.MatchID;
    }

    public static async Task<int> CreateRecoveryMatchFixtureAsync(
        BattleGridApiFactory factory,
        int player1Id,
        int player2Id,
        MatchStatus terminalStatus = MatchStatus.P1Won)
    {
        if (terminalStatus is MatchStatus.InProgress or MatchStatus.PlacingShips or MatchStatus.Loading)
            throw new ArgumentOutOfRangeException(nameof(terminalStatus), "Recovery fixture must use a terminal status.");

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();

        var match = new Match
        {
            Player1ID = player1Id,
            Player2ID = player2Id,
            Status = terminalStatus,
            TotalNoOfMoves = terminalStatus == MatchStatus.Abandoned ? 0 : 1,
            P1RatingChange = terminalStatus == MatchStatus.P1Won ? 12 : 0,
            P2RatingChange = terminalStatus == MatchStatus.P2Won ? 12 : 0,
            FinishReason = terminalStatus == MatchStatus.Abandoned
                ? "Match abandoned by system."
                : "All opponent ships were destroyed.",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
            FinishedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        await db.Match.AddAsync(match);
        await db.SaveChangesAsync();
        TrackMatch(match.MatchID);
        return match.MatchID;
    }

    public static async Task DeleteReplayableMatchFixtureAsync(BattleGridApiFactory factory, int matchId)
        => await DeleteMatchFixtureAsync(factory, matchId);

    public static async Task DeleteMatchFixtureAsync(BattleGridApiFactory factory, int matchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        await DeleteMatchOnDbAsync(db, matchId);
    }

    public static string ParseRegisterResponseBody(string body) => body.Trim().Trim('"');

    /// <summary>
    /// Registers via API; tracks email when HTTP 200 so <see cref="IntegrationApiTestBase"/> can tear down.
    /// </summary>
    public static async Task<HttpResponseMessage> RegisterAndTrackOnSuccessAsync(
        HttpClient client,
        RegisterRequestDto request)
    {
        var response = await RegisterAsync(client, request);
        if (response.IsSuccessStatusCode)
            TrackEmail(request.Email);

        return response;
    }

    public static Task CleanupTestUserAsync(BattleGridApiFactory factory, TestUserSession session) =>
        CleanupTestUserAsync(factory, session.Email);

    public static async Task CleanupTestUserAsync(BattleGridApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        await CleanupTestUserAsync(db, email);
    }

    public static async Task CleanupTestUserAsync(BattleGridDbContext db, string email)
    {
        var user = await db.User.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null)
            return;

        await DeleteMatchesForUserAsync(db, user.UserID);

        var bans = await db.BanList
            .Where(b => b.PlayerID == user.UserID
                     || b.AdminID == user.UserID
                     || b.RevertingAdminID == user.UserID)
            .ToListAsync();
        if (bans.Count > 0)
            db.BanList.RemoveRange(bans);

        var sessions = await db.Session.Where(s => s.UserID == user.UserID).ToListAsync();
        if (sessions.Count > 0)
            db.Session.RemoveRange(sessions);

        var stats = await db.PlayerStat.Where(p => p.UserID == user.UserID).ToListAsync();
        if (stats.Count > 0)
            db.PlayerStat.RemoveRange(stats);

        db.User.Remove(user);
        await db.SaveChangesAsync();
    }

    private static async Task DeleteMatchesForUserAsync(BattleGridDbContext db, int userId)
    {
        var matchIds = await db.Match
            .Where(m => m.Player1ID == userId || m.Player2ID == userId)
            .Select(m => m.MatchID)
            .ToListAsync();

        foreach (var matchId in matchIds)
            await DeleteMatchOnDbAsync(db, matchId);
    }

    private static async Task DeleteMatchOnDbAsync(BattleGridDbContext db, int matchId)
    {
        var moves = await db.MatchMove.Where(m => m.MatchID == matchId).ToListAsync();
        if (moves.Count > 0)
            db.MatchMove.RemoveRange(moves);

        var placements = await db.ShipPlacement.Where(p => p.MatchID == matchId).ToListAsync();
        if (placements.Count > 0)
            db.ShipPlacement.RemoveRange(placements);

        var spectators = await db.Spectator.Where(s => s.MatchID == matchId).ToListAsync();
        if (spectators.Count > 0)
            db.Spectator.RemoveRange(spectators);

        var match = await db.Match.FirstOrDefaultAsync(m => m.MatchID == matchId);
        if (match is not null)
            db.Match.Remove(match);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Safety net for leaked rows from past runs or failed tests. Only touches integration-test emails.
    /// </summary>
    public static async Task CleanupOrphanedIntegrationUsersAsync(BattleGridApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();

        var emails = await db.User
            .AsNoTracking()
            .Where(u => u.Email.EndsWith(TestEmailDomain))
            .Select(u => u.Email)
            .ToListAsync();

        foreach (var email in emails)
            await CleanupTestUserAsync(db, email);
    }
}

internal sealed record TestUserSession(
    RegisterRequestDto Request,
    UserResponseDto? Profile = null,
    LoginResponseDto? Tokens = null)
{
    public string Email => Request.Email;
    public string Password => Request.Password;
    public string UserName => Request.UserName;
}