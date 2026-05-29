using System.Net;
using System.Net.Http.Json;
using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;
namespace BattleGrid.Tests.Integration;
public sealed class LeaderboardEndpointTests : IntegrationApiTestBase
{
    public LeaderboardEndpointTests(BattleGridApiFactory factory) : base(factory) { }
    [Fact]
    public async Task GetCurrentSeasonLeaderboard_ReturnsOkAndValidEntries()
    {
        if (!DatabaseAvailable)
            return;
        using var response = await Client.GetAsync("/api/Leaderboard/season");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaderboardListResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(LeaderboardLimits.PageSize, payload.PageSize);
        Assert.NotNull(payload.Entries);
        Assert.True(payload.Entries.Count <= LeaderboardLimits.TopRankCount);
        Assert.All(payload.Entries, entry =>
        {
            Assert.True(entry.UserId > 0);
            Assert.True(entry.Rank > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.UserName));
            Assert.True(entry.Rating >= 0);
            Assert.True(entry.MatchesPlayed >= 0);
            Assert.True(entry.MatchesWon >= 0);
            Assert.True(entry.SeasonNo >= 1);
        });
    }
    [Fact]
    public async Task GetAllTimeLeaderboard_ReturnsOkAndValidEntries()
    {
        if (!DatabaseAvailable)
            return;
        using var response = await Client.GetAsync("/api/Leaderboard/all-time");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaderboardListResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(LeaderboardLimits.PageSize, payload.PageSize);
        Assert.NotNull(payload.Entries);
        Assert.True(payload.Entries.Count <= LeaderboardLimits.TopRankCount + LeaderboardLimits.ViewerRankContextAbove + LeaderboardLimits.ViewerRankContextBelow + 1);
        Assert.All(payload.Entries, entry =>
        {
            Assert.True(entry.UserId > 0);
            Assert.True(entry.Rank > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.UserName));
            Assert.True(entry.Rating >= 0);
            Assert.True(entry.MatchesPlayed >= 0);
            Assert.True(entry.MatchesWon >= 0);
            Assert.True(entry.SeasonNo >= 1);
        });
    }
    [Fact]
    public async Task GetCurrentSeasonLeaderboard_WhenViewerOutsideTop100_IncludesViewerContext()
    {
        if (!DatabaseAvailable)
            return;
        var viewer = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
        Assert.NotNull(viewer.Profile);
        const int playersAboveViewer = 110;
        const int playersBelowViewer = 5;
        var topFillers = new List<TestUserSession>(playersAboveViewer);
        for (var i = 0; i < playersAboveViewer; i++)
            topFillers.Add(await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client));
        var bottomFillers = new List<TestUserSession>(playersBelowViewer);
        for (var i = 0; i < playersBelowViewer; i++)
            bottomFillers.Add(await ApiIntegrationTestHelper.CreateRegisteredUserAsync(Client));
        const int viewerRating = 2890;
        var seedRows = topFillers
            .Select((session, index) => (session.Profile!.UserID, Rating: 3000 - index))
            .Append((viewer.Profile.UserID, viewerRating))
            .Concat(bottomFillers.Select((session, index) => (session.Profile!.UserID, Rating: viewerRating - 1 - index)))
            .ToList();
        var isolatedSeason = await ApiIntegrationTestHelper.GetNextGlobalSeasonNoAsync(Factory);
        await ApiIntegrationTestHelper.SeedCurrentSeasonLeaderboardAsync(Factory, seedRows, isolatedSeason);
        using var authedClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
            Factory,
            viewer.Tokens!.AccessToken);
        using var response = await authedClient.GetAsync("/api/Leaderboard/season");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaderboardListResponseDto>();
        Assert.NotNull(payload);
        Assert.True(payload.Entries.Count > LeaderboardLimits.TopRankCount);
        var viewerEntry = payload.Entries.SingleOrDefault(e => e.UserId == viewer.Profile.UserID);
        Assert.NotNull(viewerEntry);
        Assert.Equal(playersAboveViewer + 1, viewerEntry.Rank);
        Assert.True(viewerEntry.Rank > LeaderboardLimits.TopRankCount);
        Assert.Contains(payload.Entries, e => e.Rank == viewerEntry.Rank - LeaderboardLimits.ViewerRankContextAbove);
        Assert.Contains(payload.Entries, e => e.Rank == viewerEntry.Rank + LeaderboardLimits.ViewerRankContextBelow);
    }
}