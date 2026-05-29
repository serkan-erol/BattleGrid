using System.Net.Http.Json;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Tests.Integration;

public sealed class ShipTypeEndpointTests : IntegrationApiTestBase
{
    public ShipTypeEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAll_ReturnsShipList()
    {
        if (!DatabaseAvailable)
            return;

        var ships = await Client.GetFromJsonAsync<List<ShipTypeListResponseDto>>("/api/ShipType/all");

        Assert.NotNull(ships);
        Assert.NotEmpty(ships);
        Assert.All(ships, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.ShipName));
            Assert.True(s.Length > 0);
            Assert.True(s.MaxPerPlayer > 0);
        });
    }
}
