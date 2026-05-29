using System.Net.Http.Json;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Tests.Integration;

public sealed class HealthEndpointTests : IntegrationApiTestBase
{
    public HealthEndpointTests(BattleGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Root_ReturnsRunningMessage()
    {
        var body = await Client.GetStringAsync("/");
        Assert.Contains("BattleGrid API is running", body);
    }

    [Fact]
    public async Task Health_ReturnsOkWithDatabaseStatus()
    {
        if (!DatabaseAvailable)
            return;

        var health = await Client.GetFromJsonAsync<HealthResponseDto>("/health");

        Assert.NotNull(health);
        Assert.Equal("ok", health.Status);
        Assert.Equal("connected", health.Database);
    }
}
