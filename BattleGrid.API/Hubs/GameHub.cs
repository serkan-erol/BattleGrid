using BattleGrid.API.Services;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace BattleGrid.API.Hubs;

[Authorize]
public sealed class GameHub : Hub
{
    // Hubs are kind of always active. Thus, scoped services can not be directly injected because they are used, and disposed afterwards.
    // So, we inject a scope factory, use it to call any services we need to use the service
    //  and dispose the service while the scope that called them (always running hub) is till active 
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InMemoryMatchGameService _matchRuntime;

    public GameHub(IServiceScopeFactory scopeFactory, InMemoryMatchGameService matchRuntime)
    {
        _scopeFactory = scopeFactory;
        _matchRuntime = matchRuntime;
    }

    public override async Task OnConnectedAsync()
    {
        var matchId = ResolveMatchId();
        if (matchId is null)
        {
            Context.Abort();
            return;
        }

        var userId = GetUserId(Context);
        await using var scope = _scopeFactory.CreateAsyncScope();
        // Like this: Get the match services,
        var matches = scope.ServiceProvider.GetRequiredService<IMatchServices>();
        //  use it and ScopeFactory gets rid of it when its job is done
        var ok = await matches.ValidatePlayerAsync(matchId.Value, userId);
        if (!ok.Success)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, InMemoryMatchGameService.GroupName(matchId.Value), Context.ConnectionAborted);
        await Clients.Group(InMemoryMatchGameService.GroupName(matchId.Value)).SendAsync("PlayerJoined", userId, Context.ConnectionAborted);

        await _matchRuntime.OnPlayerConnectedAsync(matchId.Value, userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var matchId = ResolveMatchId();
        if (matchId is not null)
        {
            try
            {
                var userId = GetUserId(Context);
                _matchRuntime.OnPlayerDisconnected(matchId.Value, userId);
            }
            catch
            {
                // ignore identity failures on disconnect
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, InMemoryMatchGameService.GroupName(matchId.Value));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task ReadyWithPlacements(List<PlacedShipDto> placements)
    {
        var matchId = ResolveMatchId() ?? throw new HubException("Invalid match.");
        var userId = GetUserId(Context);
        await _matchRuntime.ReadyWithPlacementsAsync(matchId, userId, placements ?? new List<PlacedShipDto>());
    }

    /// <summary> Live-sync current board to the server without marking the player "ready". Timer / auto-finalize uses this. </summary>
    public async Task UpdatePlacementDraft(List<PlacedShipDto> placements)
    {
        var matchId = ResolveMatchId() ?? throw new HubException("Invalid match.");
        var userId = GetUserId(Context);
        await _matchRuntime.UpdatePlacementDraftAsync(matchId, userId, placements ?? new List<PlacedShipDto>());
    }

    public async Task FireShot(int targetX, int targetY)
    {
        var matchId = ResolveMatchId() ?? throw new HubException("Invalid match.");
        var userId = GetUserId(Context);
        await _matchRuntime.FireShotAsync(matchId, userId, targetX, targetY, Context.ConnectionId);
    }

    public Task ForfeitMatch(string? reason)
    {
        var matchId = ResolveMatchId() ?? throw new HubException("Invalid match.");
        var userId = GetUserId(Context);
        return _matchRuntime.ForfeitMatchAsync(matchId, userId, string.IsNullOrWhiteSpace(reason) ? "Forfeit" : reason!);
    }

    private int? ResolveMatchId()
    {
        var http = Context.GetHttpContext();
        if (http?.GetRouteValue("matchId") is string s && int.TryParse(s, out var id))
            return id;
        if (http?.Request.Query.TryGetValue("matchId", out var q) == true && int.TryParse(q, out var id2))
            return id2;
        return null;
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