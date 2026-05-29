using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace BattleGrid.Web.Services;

/// <summary>
/// Builds server-side SignalR client connections from the Web host to the API hubs.
/// </summary>
public static class ApiHubConnectionFactory
{
    public static HubConnection Create(IConfiguration config, AuthStateService auth, string hubPath)
    {
        var api = config["ApiBaseUrl"]?.TrimEnd('/') ?? "http://localhost:4744";
        var path = hubPath.StartsWith('/') ? hubPath : $"/{hubPath}";

        return new HubConnectionBuilder()
            .WithUrl($"{api}{path}", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(auth.AccessToken);
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                options.SkipNegotiation = false;
            })
            .WithAutomaticReconnect()
            .Build();
    }
}
