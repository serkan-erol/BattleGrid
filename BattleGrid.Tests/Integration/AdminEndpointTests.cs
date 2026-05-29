using System.Net;
using System.Net.Http.Json;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Enums;
using BattleGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BattleGrid.Tests.Integration;

public sealed class AdminEndpointTests : IntegrationApiTestBase
{
    public AdminEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task BanPlayer_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? actor = null;
        TestUserSession? target = null;
        try
        {
            actor = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, actor.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/Admin/players/ban",
                new BanRequestDto
                {
                    PlayerInfo = target.Email,
                    BanReason = "integration test",
                    IsTemporary = true,
                    Duration = TimeSpan.FromHours(1)
                });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (actor is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, actor.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task BanPlayer_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.PostAsJsonAsync("/api/Admin/players/ban",
            new BanRequestDto
            {
                PlayerInfo = "nobody@battlegrid.test",
                BanReason = "integration test",
                IsTemporary = true,
                Duration = TimeSpan.FromHours(1)
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnbanPlayer_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? actor = null;
        TestUserSession? target = null;
        try
        {
            actor = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, actor.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/Admin/players/unban",
                new UnbanPlayerRequestDto
                {
                    PlayerInfo = target.Email,
                    Reason = "integration test unban"
                });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (actor is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, actor.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }

    }

    [Fact]
    public async Task UnbanPlayer_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.PostAsJsonAsync("/api/Admin/players/unban",
            new UnbanPlayerRequestDto
            {
                PlayerInfo = "nobody@battlegrid.test",
                Reason = "integration test"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GrantAdmin_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? actor = null;
        TestUserSession? target = null;
        try
        {
            actor = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, actor.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/Admin/players/grant-admin",
                new GrantAdminRequestDto { PlayerInfo = target.Email });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (actor is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, actor.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task GrantAdmin_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.PostAsJsonAsync("/api/Admin/players/grant-admin",
            new GrantAdminRequestDto { PlayerInfo = "nobody@battlegrid.test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GrantAdmin_AsAdmin_Succeeds()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        TestUserSession? target = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/Admin/players/grant-admin",
                new GrantAdminRequestDto { PlayerInfo = target.Email });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<GeneralResponseDto>();
            Assert.NotNull(result);
            Assert.True(result.Success);

            await using var scope = Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
            var promoted = await db.User.AsNoTracking()
                .FirstAsync(u => u.UserID == target.Profile!.UserID);
            Assert.True(promoted.IsAdmin);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task GrantAdmin_AsAdmin_ReturnsBadRequest_ForUnknownPlayer()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        var missingPlayerInfo = $"missing_{Guid.NewGuid():N}@battlegrid.test";
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/Admin/players/grant-admin",
                new GrantAdminRequestDto { PlayerInfo = missingPlayerInfo });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task BanAndUnbanPlayer_AsAdmin_Succeeds()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        TestUserSession? target = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var banResponse = await authClient.PostAsJsonAsync("/api/Admin/players/ban",
                new BanRequestDto
                {
                    PlayerInfo = target.Email,
                    BanReason = "integration test ban",
                    IsTemporary = true,
                    Duration = TimeSpan.FromHours(1)
                });

            Assert.Equal(HttpStatusCode.OK, banResponse.StatusCode);
            var banResult = await banResponse.Content.ReadFromJsonAsync<GeneralResponseDto>();
            Assert.NotNull(banResult);
            Assert.True(banResult.Success);

            using var unbanResponse = await authClient.PostAsJsonAsync("/api/Admin/players/unban",
                new UnbanPlayerRequestDto
                {
                    PlayerInfo = target.Email,
                    Reason = "integration test unban"
                });

            Assert.Equal(HttpStatusCode.OK, unbanResponse.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task BanUnbanPlayer_AsAdmin_ReturnsBadRequest_ForUnknownPlayer()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        var missingPlayerInfo = $"missing_{Guid.NewGuid():N}@battlegrid.test";
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var banResponse = await authClient.PostAsJsonAsync("/api/Admin/players/ban",
                new BanRequestDto
                {
                    PlayerInfo = missingPlayerInfo,
                    BanReason = "integration unknown player",
                    IsTemporary = true,
                    Duration = TimeSpan.FromHours(1)
                });
            Assert.Equal(HttpStatusCode.BadRequest, banResponse.StatusCode);

            using var unbanResponse = await authClient.PostAsJsonAsync("/api/Admin/players/unban",
                new UnbanPlayerRequestDto
                {
                    PlayerInfo = missingPlayerInfo,
                    Reason = "integration unknown player"
                });
            Assert.Equal(HttpStatusCode.BadRequest, unbanResponse.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task GetPlayerProfile_AsAdmin_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        TestUserSession? target = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(target.Profile);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            var profile = await authClient.GetFromJsonAsync<AdminUserProfileResponseDto>(
                $"/api/Admin/players/{target.Profile.UserID}/profile");

            Assert.NotNull(profile);
            Assert.Equal(target.Profile.UserID, profile.User.UserID);
            Assert.Equal(target.UserName, profile.User.UserName);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task AdvanceSeason_AsAdmin_ReturnsOk_AndCreatesSeasonRow()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            Assert.NotNull(admin.Profile);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await ApiIntegrationTestHelper.PostAdvanceSeasonAsync(authClient);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<AdvanceSeasonResponseDto>();
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal(result.PreviousSeasonNo + 1, result.NewSeasonNo);
            Assert.True(result.AdminStartingRating > 0);

            await using (var scope = Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
                var created = await db.PlayerStat.AsNoTracking()
                    .AnyAsync(p => p.UserID == admin.Profile!.UserID && p.SeasonNo == result.NewSeasonNo);
                Assert.True(created, "Advance season should insert a PlayerStat row for the administrator.");
            }

            await ApiIntegrationTestHelper.DeletePlayerStatSeasonAsync(
                Factory, admin.Profile.UserID, result.NewSeasonNo);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task AdvanceSeason_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            Assert.NotNull(user.Profile);

            var statCountBefore = await CountPlayerStatRowsAsync(user.Profile.UserID);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            using var response = await ApiIntegrationTestHelper.PostAdvanceSeasonAsync(authClient);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

            var statCountAfter = await CountPlayerStatRowsAsync(user.Profile.UserID);
            Assert.Equal(statCountBefore, statCountAfter);
        }
        finally
        {
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
        }
    }

    [Fact]
    public async Task AdvanceSeason_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await ApiIntegrationTestHelper.PostAdvanceSeasonAsync(Client);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPlayerMatchHistory_AsAdmin_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        TestUserSession? target = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            target = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(target.Profile);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            var history = await authClient.GetFromJsonAsync<MatchHistoryResponseDto>(
                $"/api/Admin/players/{target.Profile.UserID}/match-history?limit=5");

            Assert.NotNull(history);
            Assert.NotNull(history.Matches);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
            if (target is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, target.Email);
        }
    }

    [Fact]
    public async Task GetPlayerMatchHistory_AsAdmin_ReturnsNotFound_ForUnknownPlayer()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/players/999999/match-history?limit=5");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task GetPlayerMatchHistory_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/players/1/match-history?limit=5");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
        }
    }

    [Fact]
    public async Task GetPlayerProfile_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Admin/players/1/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPlayerProfile_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/players/1/profile");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
        }
    }

    [Fact]
    public async Task GetPlayerProfile_AsAdmin_ReturnsNotFound_ForUnknownPlayer()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/players/999999/profile");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task GetPlayerMatchHistory_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Admin/players/1/match-history?limit=5");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminMatchReplay_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Admin/matches/999999/replay");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminMatchReplay_AsAdmin_ReturnsNotFound_ForUnknownMatch()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/matches/999999/replay");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
        }
    }

    [Fact]
    public async Task GetAdminMatchReplay_AsAdmin_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? admin = null;
        TestUserSession? player1 = null;
        TestUserSession? player2 = null;
        int? matchId = null;

        try
        {
            admin = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory, asAdmin: true);
            player1 = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            player2 = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(player1.Profile);
            Assert.NotNull(player2.Profile);

            matchId = await ApiIntegrationTestHelper.CreateReplayableMatchFixtureAsync(
                Factory,
                player1.Profile!.UserID,
                player2.Profile!.UserID);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, admin.Tokens!.AccessToken);

            using var response = await authClient.GetAsync($"/api/Admin/matches/{matchId.Value}/replay");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var replay = await response.Content.ReadFromJsonAsync<MatchReplayResponseDto>();
            Assert.NotNull(replay);
            Assert.Equal(matchId.Value, replay.MatchId);
            Assert.Equal(player1.Profile.UserID, replay.Player1Id);
            Assert.Equal(player2.Profile.UserID, replay.Player2Id);
            Assert.NotEmpty(replay.Placements);
            Assert.NotEmpty(replay.Moves);
            Assert.Equal((int)MatchStatus.P1Won, replay.Status);
        }
        finally
        {
            if (matchId is int createdMatchId)
                await ApiIntegrationTestHelper.DeleteReplayableMatchFixtureAsync(Factory, createdMatchId);

            if (admin is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, admin.Email);
            if (player1 is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, player1.Email);
            if (player2 is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, player2.Email);
        }
    }

    [Fact]
    public async Task GetAdminMatchReplay_AsNonAdmin_ReturnsForbidden()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Admin/matches/999999/replay");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
        }
    }

    private async Task<int> CountPlayerStatRowsAsync(int userId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
        return await db.PlayerStat.CountAsync(p => p.UserID == userId);
    }

}