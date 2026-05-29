using BattleGrid.API.Matchmaking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BattleGrid.API.Hubs;

[Authorize]
public sealed class QueueHub : Hub
{
    private readonly MatchmakingCoordinator _matchmaking;

    public QueueHub(MatchmakingCoordinator matchmaking)
    {
        _matchmaking = matchmaking;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId(Context);
        await _matchmaking.OnQueueConnectedAsync(Context.ConnectionId, userId, Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }

    public Task VoluntaryLeaveQueue()
    {
        var userId = GetUserId(Context);
        _matchmaking.RemoveFromQueueVoluntarily(userId);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _matchmaking.OnQueueDisconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private static int GetUserId(HubCallerContext context)
    {
        var id = context.User?.FindFirst("userId")?.Value
                 ?? context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (id is null || !int.TryParse(id, out var userId))
            throw new HubException("Invalid or missing user identity.");
        return userId;
    }
}