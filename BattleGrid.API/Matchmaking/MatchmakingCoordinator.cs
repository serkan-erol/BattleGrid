using BattleGrid.API.Hubs;
using BattleGrid.Application.Interfaces;
using BattleGrid.Domain;
using BattleGrid.Domain.Entities;
using BattleGrid.Domain.Enums;
using BattleGrid.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;

namespace BattleGrid.API.Matchmaking;

public sealed class MatchmakingCoordinator
{
    private static readonly TimeSpan WindowStep = TimeSpan.FromSeconds(30);
    private const int InitialHalfWindow = 200;      // Initially we try to find an opponent in +-200 rating range
    private const int WindowGrowthPerStep = 200;    // Every 30 seconds we increase this range by another 200 rating (+-400, then +-600 and so on)
    private const int MaxHalfWindow = 1000;         // Opponent search ranking range is maxed at +-1000 to keep it somewhat fair

    private readonly IHubContext<QueueHub> _queueHub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly List<WaitingEntry> _waiting = new();
    /// <summary> Preserves queue join time across brief disconnects (e.g. full page refresh). </summary>
    private readonly Dictionary<int, (DateTimeOffset JoinedAtUtc, DateTimeOffset DisconnectedAtUtc)> _rejoinGrace = new();
    private readonly object _lock = new();

    private static readonly TimeSpan RejoinGraceWindow = TimeSpan.FromSeconds(10);

    public MatchmakingCoordinator(IHubContext<QueueHub> queueHub, IServiceScopeFactory scopeFactory)
    {
        _queueHub = queueHub;
        _scopeFactory = scopeFactory;
    }

    public async Task OnQueueConnectedAsync(string connectionId, int userId, CancellationToken cancellationToken = default)
    {
        int rating;
        int? blockingMatchId;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var seasonSvc = scope.ServiceProvider.GetRequiredService<IPlayerStatSeasonService>();
            var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
            var state = await seasonSvc.EnsureMatchmakingRatingAsync(userId, cancellationToken);
            rating = state.RatingForQueue;
            blockingMatchId = await matchSvc.GetResumableMatchIdAsync(userId);
        }

        if (blockingMatchId is int mid)
        {
            await _queueHub.Clients.Client(connectionId)
                .SendAsync("QueueRejected", mid, cancellationToken);
            return;
        }

        DateTimeOffset joinedAtUtc;
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            foreach (var key in _rejoinGrace.Keys.ToList())
            {
                if (now - _rejoinGrace[key].DisconnectedAtUtc > RejoinGraceWindow)
                    _rejoinGrace.Remove(key);
            }

            _waiting.RemoveAll(w => w.ConnectionId == connectionId || w.UserId == userId);
            if (_rejoinGrace.TryGetValue(userId, out var grace) && now - grace.DisconnectedAtUtc <= RejoinGraceWindow)
            {
                joinedAtUtc = grace.JoinedAtUtc;
                _rejoinGrace.Remove(userId);
            }
            else
            {
                _rejoinGrace.Remove(userId);
                joinedAtUtc = now;
            }

            _waiting.Add(new WaitingEntry(connectionId, userId, joinedAtUtc, rating));
        }

        await _queueHub.Clients.Client(connectionId)
            .SendAsync("QueueJoined", joinedAtUtc.ToUnixTimeMilliseconds(), rating, cancellationToken);

        await TryMatchWaitingPlayersAsync(cancellationToken);
    }

    /// <summary>
    /// Attempts to form matches for all compatible pairs in the queue (greedy, oldest waiters first).
    /// </summary>
    public async Task TryMatchWaitingPlayersAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var now = DateTimeOffset.UtcNow.ToUniversalTime();
            WaitingEntry? first = null;
            WaitingEntry? second = null;

            lock (_lock)
            {
                /* Get player list in the queue ordered ASC joining time.
                 * So, we try to match FIFO style to keep the waiting times reasonable
                 * meanwhile making sure matched players are in each other's search range
                 */
                var ordered = _waiting
                    .OrderBy(w => w.JoinedAtUtc)
                    .ThenBy(w => w.UserId)
                    .ToList();

                /* 1 - Take the first player in the list
                 * 2 - Check every other player in the queue
                 * 3 - If we find a match, remove them from the queue
                 * 4 - Take the next avaliable player from the queue, back to step 1
                 */
                for (var i = 0; i < ordered.Count && first is null; i++)
                {

                    for (var j = i + 1; j < ordered.Count; j++)
                    {
                        if (RatingsMutuallyAcceptable(ordered[i], ordered[j], now))
                        {
                            first = ordered[i];
                            second = ordered[j];
                            _waiting.RemoveAll(w =>
                                w.ConnectionId == first.ConnectionId || w.ConnectionId == second.ConnectionId);
                            _rejoinGrace.Remove(first.UserId);
                            _rejoinGrace.Remove(second.UserId);
                            break;
                        }
                    }
                }
            }

            if (first is null || second is null)
                return;

            var earlier = first.JoinedAtUtc <= second.JoinedAtUtc ? first : second;
            var later = ReferenceEquals(earlier, first) ? second : first;

            int matchId;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
                var match = new Match
                {
                    Player1ID = earlier.UserId,
                    Player2ID = later.UserId,
                    Status = MatchStatus.Loading,
                    StartedAt = DateTimeOffset.UtcNow
                };
                db.Match.Add(match);
                await db.SaveChangesAsync(cancellationToken);
                matchId = match.MatchID;
            }

            await _queueHub.Clients.Client(earlier.ConnectionId)
                .SendAsync("Matched", matchId, later.UserId, cancellationToken);
            await _queueHub.Clients.Client(later.ConnectionId)
                .SendAsync("Matched", matchId, earlier.UserId, cancellationToken);
        }
    }

    /// <summary>
    /// Called when the client explicitly leaves matchmaking. Removes the player without recording
    /// rejoin grace so the next queue session starts with a fresh join time.
    /// </summary>
    public void RemoveFromQueueVoluntarily(int userId)
    {
        lock (_lock)
        {
            _rejoinGrace.Remove(userId);
            _waiting.RemoveAll(w => w.UserId == userId);
        }
    }

    public void OnQueueDisconnected(string connectionId)
    {
        lock (_lock)
        {
            var removed = _waiting.FirstOrDefault(w => w.ConnectionId == connectionId);
            _waiting.RemoveAll(w => w.ConnectionId == connectionId);
            if (removed is not null)
                _rejoinGrace[removed.UserId] = (removed.JoinedAtUtc, DateTimeOffset.UtcNow.ToUniversalTime());
        }
    }

    /// <summary>
    /// Half-width of the rating window: ±value around the player's rating.
    /// First 30s: 200, then +200 each 30s, capped at 1000.
    /// </summary>
    internal static int GetRatingHalfWindow(TimeSpan elapsedInQueue)
    {
        if (elapsedInQueue < TimeSpan.Zero)
            elapsedInQueue = TimeSpan.Zero;

        var periods = (int)Math.Floor(elapsedInQueue.TotalSeconds / WindowStep.TotalSeconds);
        var half = InitialHalfWindow + periods * WindowGrowthPerStep;
        return Math.Min(MaxHalfWindow, half);
    }

    /// <summary> 
    /// When we find an opponent in the current player's search range, we check if the player is in opponent's search range, as well.
    /// </summary>

    /* For example: 
     * P4 has 1600 rating and in the queue for 1m 20s => P4 will accept someone with 1600 ± 600 rating as suitable opponent
     * P27 has 1000 rating and in the queue for 10s   => P27 will accept someone with 1000 ± 200 rating as suitable opponent
     * So, P4 will see P28 as suitable opponent
     * However, P4 is actually not suitable for P27 (yet)
     * So, we do not match them and look for another opponent
     * If we can not find any other opponent for both until P27 is in the queue long enough to accept ±600 range, 
     *  then we can match them with each other
     */
    internal static bool RatingsMutuallyAcceptable(WaitingEntry player, WaitingEntry possibleOpponent, DateTimeOffset now)
    {
        var playerRating = PlayerRatingBounds.ClampRating(player.Rating);
        var possibleOpponentRating = PlayerRatingBounds.ClampRating(possibleOpponent.Rating);
        var diff = Math.Abs(playerRating - possibleOpponentRating);
        var wa = GetRatingHalfWindow(now - player.JoinedAtUtc);
        var wb = GetRatingHalfWindow(now - possibleOpponent.JoinedAtUtc);
        return diff <= wa && diff <= wb;
    }

    /// <summary> 
    /// A player's info in the queue 
    /// Queue will be consisting of a list of WAitingEntry records
    /// </summary>
    internal sealed record WaitingEntry(
        string ConnectionId,
        int UserId,
        DateTimeOffset JoinedAtUtc,
        int Rating);
}