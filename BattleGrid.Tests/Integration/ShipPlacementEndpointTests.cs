using System.Net;
using System.Net.Http.Json;
using BattleGrid.Contracts.RequestDtos;

namespace BattleGrid.Tests.Integration;

public sealed class ShipPlacementEndpointTests : IntegrationApiTestBase
{
    public ShipPlacementEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task PlaceShip_WithoutAuth_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        using var response = await Client.PostAsJsonAsync("/api/ShipPlacement/placeShip",
            new PlaceShipRequestDto
            {
                PlayerID = 1,
                MatchID = 1,
                ShipID = 1,
                StartX = 0,
                StartY = 0,
                IsVertical = false
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PlaceShip_WithAuth_AndInvalidMatch_ReturnsUnauthorized()
    {
        if (!DatabaseAvailable)
            return;

        TestUserSession? session = null;
        try
        {
            session = await ApiIntegrationTestHelper.CreateLoggedInUserAsync(Client, Factory);
            Assert.NotNull(session.Profile);

            using var authClient = ApiIntegrationTestHelper.CreateAuthenticatedClient(
                Factory, session.Tokens!.AccessToken);

            using var response = await authClient.PostAsJsonAsync("/api/ShipPlacement/placeShip",
                new PlaceShipRequestDto
                {
                    PlayerID = session.Profile!.UserID,
                    MatchID = 999999,
                    ShipID = 1,
                    StartX = 0,
                    StartY = 0,
                    IsVertical = false
                });

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            if (session is not null)
                await ApiIntegrationTestHelper.CleanupTestUserAsync(Factory, session.Email);
        }
    }
}