using System.Net;
using System.Net.Http.Json;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Enums;

namespace BattleGrid.Tests.Integration;

public sealed class MatchEndpointTests : IntegrationApiTestBase
{
    public MatchEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetResumable_WithAuth_AndNoActiveMatch_ReturnsNoContent()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? session = null;
        try
        {
            session = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, session.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Match/resumable");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            if (session is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, session.Email);
        }
    }

    [Fact]
    public async Task GetResumable_WithAuth_ReturnsOk_AndRelatedData()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        TestUserSession? opponent = null;
        int? matchId = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            opponent = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(user.Profile);
            Assert.NotNull(opponent.Profile);

            matchId = await ApiIntegrationTestHelper.CreateResumableMatchFixtureAsync(
                Factory, user.Profile!.UserID, opponent.Profile!.UserID, MatchStatus.InProgress);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            var dto = await authClient.GetFromJsonAsync<ResumableMatchResponseDto>("/api/Match/resumable");
            Assert.NotNull(dto);
            Assert.Equal(matchId.Value, dto.MatchId);
        }
        finally
        {
            if (matchId is int createdMatchId)
                await ApiIntegrationTestHelper.DeleteMatchFixtureAsync(Factory, createdMatchId);
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
            if (opponent is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, opponent.Email);
        }
    }

    [Fact]
    public async Task GetResumable_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Match/resumable");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_WithAuth_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? session = null;
        try
        {
            session = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, session.Tokens!.AccessToken);

            var history = await authClient.GetFromJsonAsync<MatchHistoryResponseDto>(
                "/api/Match/history?limit=10");

            Assert.NotNull(history);
            Assert.NotNull(history.Matches);
        }
        finally
        {
            if (session is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, session.Email);
        }
    }

    [Fact]
    public async Task GetHistory_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Match/history?limit=10");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReplay_ForUnknownMatch_ReturnsNotFound()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? session = null;
        try
        {
            session = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, session.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Match/999999/replay");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            if (session is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, session.Email);
        }
    }

    [Fact]
    public async Task GetReplay_WithAuth_ReturnsOk()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? player1 = null;
        TestUserSession? player2 = null;
        int? matchId = null;
        try
        {
            player1 = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            player2 = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(player1.Profile);
            Assert.NotNull(player2.Profile);

            matchId = await ApiIntegrationTestHelper.CreateReplayableMatchFixtureAsync(
                Factory, player1.Profile!.UserID, player2.Profile!.UserID);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, player1.Tokens!.AccessToken);

            var replay = await authClient.GetFromJsonAsync<MatchReplayResponseDto>(
                $"/api/Match/{matchId.Value}/replay");

            Assert.NotNull(replay);
            Assert.Equal(matchId.Value, replay.MatchId);
            Assert.Equal(player1.Profile.UserID, replay.Player1Id);
            Assert.Equal(player2.Profile.UserID, replay.Player2Id);
            Assert.NotEmpty(replay.Moves);
            Assert.NotEmpty(replay.Placements);
        }
        finally
        {
            if (matchId is int createdMatchId)
                await ApiIntegrationTestHelper.DeleteMatchFixtureAsync(Factory, createdMatchId);
            if (player1 is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, player1.Email);
            if (player2 is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, player2.Email);
        }
    }

    [Fact]
    public async Task GetReplay_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Match/999999/replay");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecovery_ForUnknownMatch_ReturnsNoContent()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? session = null;
        try
        {
            session = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, session.Tokens!.AccessToken);

            using var response = await authClient.GetAsync("/api/Match/999999/recovery");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        finally
        {
            if (session is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, session.Email);
        }
    }

    [Fact]
    public async Task GetRecovery_WithAuth_ReturnsOk_AndMatchData()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? user = null;
        TestUserSession? opponent = null;
        int? matchId = null;
        try
        {
            user = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            opponent = await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client);
            Assert.NotNull(user.Profile);
            Assert.NotNull(opponent.Profile);

            matchId = await ApiIntegrationTestHelper.CreateRecoveryMatchFixtureAsync(
                Factory, user.Profile!.UserID, opponent.Profile!.UserID, MatchStatus.P1Won);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, user.Tokens!.AccessToken);

            var recovery = await authClient.GetFromJsonAsync<MatchRecoveryStateDto>(
                $"/api/Match/{matchId.Value}/recovery");

            Assert.NotNull(recovery);
            Assert.Equal(matchId.Value, recovery.MatchId);
            Assert.Equal((int)MatchStatus.P1Won, recovery.Status);
            Assert.Equal(user.Profile.UserID, recovery.Player1Id);
            Assert.Equal(opponent.Profile.UserID, recovery.Player2Id);
            Assert.Equal(user.Profile.UserID, recovery.WinnerUserId);
        }
        finally
        {
            if (matchId is int createdMatchId)
                await ApiIntegrationTestHelper.DeleteMatchFixtureAsync(Factory, createdMatchId);
            if (user is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, user.Email);
            if (opponent is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, opponent.Email);
        }
    }

    [Fact]
    public async Task GetRecovery_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.GetAsync("/api/Match/999999/recovery");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}