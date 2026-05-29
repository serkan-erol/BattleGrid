using BattleGrid.API.Hubs;
using BattleGrid.Application.Interfaces;
using BattleGrid.Contracts.RequestDtos;
using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;
using BattleGrid.Domain.Enums;
using BattleGrid.Domain.GameLogic;
using BattleGrid.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace BattleGrid.API.Services;

public sealed class InMemoryMatchGameService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ConcurrentDictionary<int, MatchRoom> _rooms = new();

    public InMemoryMatchGameService(IServiceScopeFactory scopeFactory, IHubContext<GameHub> hubContext)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
    }

    public static string GroupName(int matchId) => $"match_{matchId}";

    public async Task OnPlayerConnectedAsync(int matchId, int userId, string connectionId)
    {
        var room = _rooms.GetOrAdd(matchId, id => new MatchRoom(id, _scopeFactory, _hubContext));
        await room.OnPlayerConnectedAsync(userId, connectionId);
    }

    public void OnPlayerDisconnected(int matchId, int userId)
    {
        if (_rooms.TryGetValue(matchId, out var room))
            room.OnPlayerDisconnected(userId);
    }

    public Task ReadyWithPlacementsAsync(int matchId, int userId, IReadOnlyList<PlacedShipDto> placements) =>
        _rooms.TryGetValue(matchId, out var room)
            ? room.ReadyWithPlacementsAsync(userId, placements)
            : Task.CompletedTask;

    public Task UpdatePlacementDraftAsync(int matchId, int userId, IReadOnlyList<PlacedShipDto> placements) =>
        _rooms.TryGetValue(matchId, out var room)
            ? room.UpdatePlacementDraftAsync(userId, placements)
            : Task.CompletedTask;

    public Task<ShotResult?> FireShotAsync(int matchId, int userId, int targetX, int targetY, string callerConnectionId) =>
        _rooms.TryGetValue(matchId, out var room)
            ? room.FireShotAsync(userId, targetX, targetY, callerConnectionId)
            : Task.FromResult<ShotResult?>(null);

    public Task ForfeitMatchAsync(int matchId, int userId, string reason) =>
        _rooms.TryGetValue(matchId, out var room)
            ? room.ForfeitAsync(userId, reason)
            : Task.CompletedTask;

    private sealed class MatchRoom
    {
        private readonly int _matchId;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<GameHub> _hubContext;

        private readonly object _gate = new();
        private readonly Dictionary<int, string> _userConnections = new();
        private readonly Dictionary<int, List<PlacedShipDto>> _drafts = new();
        private readonly HashSet<int> _ready = new();

        private int _player1Id;
        private int _player2Id;
        private List<ShipType>? _fleet;
        private int _shipsPerPlayer;
        private DateTimeOffset? _placementDeadlineUtc;
        private CancellationTokenSource? _placementTimerCts;
        private bool _placementBroadcastStarted;
        private bool _battleStarted;
        private GameState? _game;
        private readonly SemaphoreSlim _placementFinalize = new(1, 1);
        private readonly List<(int shooterId, int moveNumber, int x, int y, bool hit)> _recordedMoves = new();
        private bool _matchPersisted;
        private readonly List<PlacedShipDto> _p1FinalPlacements = new();
        private readonly List<PlacedShipDto> _p2FinalPlacements = new();
        private bool _matchFinished;
        private CancellationTokenSource? _shotClockLoopCts;
        private DateTimeOffset _shotClockDeadlineUtc;
        private CancellationTokenSource? _disconnectGraceCts;
        private int? _disconnectGraceUserId;
        /// <summary> Per-user consecutive forced-shot/clock-misses while it was their turn. Reset on any voluntary shot. </summary>
        private readonly Dictionary<int, int> _afkTimeoutStreak = new();

        private bool _processingForcedShotExpiry;
        /// <summary>After API restart, battle state was rebuilt from DB - broadcast once when a player reconnects.</summary>
        private bool _pendingRestoredBattleBroadcast;

        public MatchRoom(int matchId, IServiceScopeFactory scopeFactory, IHubContext<GameHub> hubContext)
        {
            _matchId = matchId;
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
        }

        public async Task OnPlayerConnectedAsync(int userId, string connectionId)
        {
            await EnsureMatchAndFleetAsync();

            bool startTimer;
            lock (_gate)
            {
                _userConnections[userId] = connectionId;
                if (_disconnectGraceUserId == userId)
                {
                    _disconnectGraceUserId = null;
                    _disconnectGraceCts?.Cancel();
                    _disconnectGraceCts?.Dispose();
                    _disconnectGraceCts = null;
                }

                startTimer = !_matchFinished
                    && !_placementBroadcastStarted
                    && !_battleStarted
                    && _userConnections.Count >= 2;
                if (startTimer)
                {
                    _placementBroadcastStarted = true;
                    _placementDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(45);
                    _placementTimerCts = new CancellationTokenSource();
                    _ = PlacementTimerAsync(_placementTimerCts.Token);
                }
            }

            if (startTimer && _placementDeadlineUtc is { } deadline)
            {
                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
                    await matchSvc.TrySetMatchStatusAsync(_matchId, MatchStatus.PlacingShips);
                }

                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("PlacementPhaseStarted", new
                {
                    deadlineUtc = deadline,
                    seconds = 45,
                    shipsPerPlayer = _shipsPerPlayer
                });
            }

            GameState? battleGame;
            bool broadcastRestoredBattle;
            lock (_gate)
            {
                battleGame = _game;
                broadcastRestoredBattle = _pendingRestoredBattleBroadcast;
                _pendingRestoredBattleBroadcast = false;
            }

            if (broadcastRestoredBattle && battleGame is not null)
            {
                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("BattleStarted", new
                {
                    player1Id = battleGame.Player1Id,
                    player2Id = battleGame.Player2Id,
                    currentTurnPlayerId = battleGame.CurrentTurnPlayerId,
                    phase = battleGame.Phase.ToString()
                });
                StartShotClockLoop();
                await FanoutBattleSnapshotsAsync(battleGame);
            }
            else if (_battleStarted && battleGame is not null)
            {
                await SendBattleSnapshotToClientAsync(battleGame, userId, connectionId);
            }
        }

        public void OnPlayerDisconnected(int userId)
        {
            bool startGrace = false;
            var bothDisconnected = false;
            lock (_gate)
            {
                _userConnections.Remove(userId);
                if (_matchFinished)
                    return;
                if (!_placementBroadcastStarted && !_battleStarted)
                    return;

                /* No SignalR clients left - abandon in DB with no winner (cannot run forfeits). */
                if (_userConnections.Count == 0)
                {
                    bothDisconnected = true;
                    return;
                }

                var opponentId = userId == _player1Id ? _player2Id : _player1Id;
                if (_userConnections.ContainsKey(opponentId))
                {
                    startGrace = true;
                    _disconnectGraceUserId = userId;
                    _disconnectGraceCts?.Cancel();
                    _disconnectGraceCts?.Dispose();
                    _disconnectGraceCts = new CancellationTokenSource();
                }
            }

            if (bothDisconnected)
            {
                _ = FinalizeMatchBothPlayersDisconnectedAsync();
                return;
            }

            if (startGrace && _disconnectGraceCts is not null)
                _ = RunDisconnectGraceAsync(userId, _disconnectGraceCts.Token);
        }

        public async Task ReadyWithPlacementsAsync(int userId, IReadOnlyList<PlacedShipDto> placements)
        {
            await EnsureMatchAndFleetAsync();

            lock (_gate)
            {
                if (_battleStarted)
                    return;
                _drafts[userId] = placements.ToList();
                _ready.Add(userId);
            }

            if (BothPlayersReady())
                await TryFinalizePlacementAsync();
        }

        public async Task UpdatePlacementDraftAsync(int userId, IReadOnlyList<PlacedShipDto> placements)
        {
            await EnsureMatchAndFleetAsync();

            var bothComplete = false;
            lock (_gate)
            {
                if (_battleStarted || _fleet is null)
                    return;
                _drafts[userId] = placements.ToList();
                var required = ExpandFleetInstances(_fleet);
                bothComplete = HasValidFullDraftLocked(_player1Id, required) && HasValidFullDraftLocked(_player2Id, required);
            }

            if (bothComplete)
                await TryFinalizePlacementAsync();
        }

        private bool HasValidFullDraftLocked(int playerId, List<(int ShipID, int Length, int Width)> required)
        {
            if (_fleet is null)
                return false;
            if (!_drafts.TryGetValue(playerId, out var list) || list.Count == 0)
                return false;
            return IsValidDraft(list, required, _fleet);
        }

        private bool BothPlayersReady()
        {
            lock (_gate)
            {
                return _ready.Contains(_player1Id) && _ready.Contains(_player2Id);
            }
        }

        private async Task PlacementTimerAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(45), ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            await TryFinalizePlacementAsync();
        }

        private async Task TryFinalizePlacementAsync()
        {
            if (!await _placementFinalize.WaitAsync(0))
                return;

            List<(int PlayerID, int ShipID, int StartX, int StartY, bool IsVertical)>? rowsToSave = null;
            GameState? newGame = null;
            List<ShipType>? fleetCopy;

            try
            {
                lock (_gate)
                {
                    if (_battleStarted || _fleet is null)
                        return;
                    fleetCopy = _fleet.ToList();
                }

                await EnsureMatchAndFleetAsync();

                List<PlacedShipDto> p1Final;
                List<PlacedShipDto> p2Final;
                lock (_gate)
                {
                    if (_battleStarted)
                        return;
                    p1Final = ResolvePlacementsForPlayer(_drafts.GetValueOrDefault(_player1Id), _fleet!);
                    p2Final = ResolvePlacementsForPlayer(_drafts.GetValueOrDefault(_player2Id), _fleet!);
                    _p1FinalPlacements.Clear();
                    _p1FinalPlacements.AddRange(p1Final);
                    _p2FinalPlacements.Clear();
                    _p2FinalPlacements.AddRange(p2Final);
                }

                rowsToSave = new List<(int, int, int, int, bool)>();
                foreach (var p in p1Final)
                    rowsToSave.Add((_player1Id, p.ShipID, p.StartX, p.StartY, p.IsVertical));
                foreach (var p in p2Final)
                    rowsToSave.Add((_player2Id, p.ShipID, p.StartX, p.StartY, p.IsVertical));

                newGame = new GameState(_player1Id, _player2Id, _shipsPerPlayer);
                ApplyPlacementsToGameState(newGame, _player1Id, p1Final, fleetCopy);
                ApplyPlacementsToGameState(newGame, _player2Id, p2Final, fleetCopy);

                await using (var scope = _scopeFactory.CreateAsyncScope())
                {
                    try
                    {
                        var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
                        var dbMatch = await db.Match.AsNoTracking()
                            .FirstOrDefaultAsync(m => m.MatchID == _matchId);
                        if (dbMatch is null)
                            return;
                        if (dbMatch.Status == MatchStatus.InProgress)
                            return;

                        var placementsSvc = scope.ServiceProvider.GetRequiredService<IShipPlacementServices>();
                        var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
                        await placementsSvc.SavePlacementsBatchAsync(_matchId, rowsToSave);
                        await matchSvc.TrySetMatchStatusAsync(_matchId, MatchStatus.InProgress);
                    }
                    catch (Exception ex)
                    {
                        await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId))
                            .SendAsync("PlacementSaveFailed", ex.Message);
                        return;
                    }
                }

                lock (_gate)
                {
                    if (_battleStarted)
                        return;
                    _placementTimerCts?.Cancel();
                    _placementTimerCts?.Dispose();
                    _placementTimerCts = null;
                    _game = newGame;
                    _battleStarted = true;
                }

                await FanoutFleetLayoutsAsync(fleetCopy);

                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("BattleStarted", new
                {
                    player1Id = newGame.Player1Id,
                    player2Id = newGame.Player2Id,
                    currentTurnPlayerId = newGame.CurrentTurnPlayerId,
                    phase = newGame.Phase.ToString()
                });

                StartShotClockLoop();
                await FanoutBattleSnapshotsAsync(newGame);
            }
            finally
            {
                _placementFinalize.Release();
            }
        }

        private static void ApplyPlacementsToGameState(GameState game, int playerId, List<PlacedShipDto> list, IReadOnlyList<ShipType> fleetLookup)
        {
            var byId = fleetLookup.ToDictionary(s => s.ShipID);
            foreach (var dto in list)
            {
                if (!byId.TryGetValue(dto.ShipID, out var st))
                    throw new InvalidOperationException($"Unknown ShipID {dto.ShipID}");

                if (!ShipPlacementHelper.TryGenerateRectangle(
                        new Coordinate(dto.StartX, dto.StartY),
                        st.Length,
                        st.Width,
                        dto.IsVertical,
                        out var cells) || cells is null)
                    throw new InvalidOperationException("Invalid placement replay.");

                game.PlaceShip(playerId, cells);
            }
        }

        private static List<PlacedShipDto> ResolvePlacementsForPlayer(List<PlacedShipDto>? draft, IReadOnlyList<ShipType> fleet)
        {
            var required = ExpandFleetInstances(fleet);
            var work = draft ?? new List<PlacedShipDto>();

            if (IsValidDraft(work, required, fleet))
                return work;

            if (TryCompleteDraftWithRandomFill(work, required, fleet, out var merged) && merged is not null)
                return merged;

            return GenerateFullRandomLayout(required);
        }

        private static bool TryCompleteDraftWithRandomFill(
            List<PlacedShipDto> draft,
            List<(int ShipID, int Length, int Width)> requiredInstances,
            IReadOnlyList<ShipType> fleet,
            [NotNullWhen(true)] out List<PlacedShipDto>? merged)
        {
            merged = null;
            var byId = fleet.ToDictionary(s => s.ShipID);
            var occ = new bool[Board.GridSize, Board.GridSize];
            var kept = new List<PlacedShipDto>();

            foreach (var p in draft)
            {
                if (!byId.TryGetValue(p.ShipID, out var st))
                    return false;
                if (!ShipPlacementHelper.TryGenerateRectangle(new Coordinate(p.StartX, p.StartY), st.Length, st.Width, p.IsVertical, out var cells) ||
                    cells is null)
                    return false;

                foreach (var c in cells)
                {
                    if (occ[c.X, c.Y])
                        return false;
                    occ[c.X, c.Y] = true;
                }

                kept.Add(p);
            }

            var needCounts = requiredInstances.GroupBy(x => x.ShipID).ToDictionary(g => g.Key, g => g.Count());
            var haveCounts = kept.GroupBy(d => d.ShipID).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in haveCounts)
            {
                if (!needCounts.TryGetValue(kv.Key, out var mx) || kv.Value > mx)
                    return false;
            }

            var pool = new List<(int ShipID, int Length, int Width)>(requiredInstances);
            foreach (var p in kept)
            {
                var idx = pool.FindIndex(t => t.ShipID == p.ShipID);
                if (idx < 0)
                    return false;
                pool.RemoveAt(idx);
            }

            if (pool.Count == 0)
            {
                merged = kept;
                return true;
            }

            var rnd = Random.Shared;
            foreach (var inst in pool)
            {
                var placed = false;
                for (var attempt = 0; attempt < 2000 && !placed; attempt++)
                {
                    var vert = rnd.Next(2) == 0;
                    var sx = rnd.Next(Board.GridSize);
                    var sy = rnd.Next(Board.GridSize);
                    if (!ShipPlacementHelper.TryGenerateRectangle(new Coordinate(sx, sy), inst.Length, inst.Width, vert, out var cells) ||
                        cells is null)
                        continue;

                    if (cells.Any(c => occ[c.X, c.Y]))
                        continue;

                    foreach (var c in cells)
                        occ[c.X, c.Y] = true;

                    kept.Add(new PlacedShipDto
                    {
                        ShipID = inst.ShipID,
                        StartX = sx,
                        StartY = sy,
                        IsVertical = vert
                    });
                    placed = true;
                }

                if (!placed)
                    return false;
            }

            merged = kept;
            return true;
        }

        private static List<(int ShipID, int Length, int Width)> ExpandFleetInstances(IReadOnlyList<ShipType> fleet)
        {
            var list = new List<(int, int, int)>();
            foreach (var s in fleet)
            {
                for (var i = 0; i < s.MaxPerPlayer; i++)
                    list.Add((s.ShipID, s.Length, s.Width));
            }

            return list;
        }

        private static bool IsValidDraft(List<PlacedShipDto> draft, List<(int ShipID, int Length, int Width)> requiredInstances, IReadOnlyList<ShipType> fleet)
        {
            if (draft.Count != requiredInstances.Count)
                return false;

            var needCounts = requiredInstances.GroupBy(x => x.ShipID).ToDictionary(g => g.Key, g => g.Count());
            var haveCounts = draft.GroupBy(d => d.ShipID).ToDictionary(g => g.Key, g => g.Count());
            if (needCounts.Count != haveCounts.Count)
                return false;
            foreach (var kv in needCounts)
            {
                if (!haveCounts.TryGetValue(kv.Key, out var c) || c != kv.Value)
                    return false;
            }

            var byId = fleet.ToDictionary(s => s.ShipID);
            var occ = new bool[Board.GridSize, Board.GridSize];
            foreach (var p in draft)
            {
                if (!byId.TryGetValue(p.ShipID, out var st))
                    return false;

                if (!ShipPlacementHelper.TryGenerateRectangle(
                        new Coordinate(p.StartX, p.StartY),
                        st.Length,
                        st.Width,
                        p.IsVertical,
                        out var cells) || cells is null)
                    return false;

                foreach (var c in cells)
                {
                    if (occ[c.X, c.Y])
                        return false;
                    occ[c.X, c.Y] = true;
                }
            }

            return true;
        }

        private static List<PlacedShipDto> GenerateFullRandomLayout(List<(int ShipID, int Length, int Width)> instances)
        {
            var rnd = Random.Shared;
            var result = new List<PlacedShipDto>();
            var occ = new bool[Board.GridSize, Board.GridSize];

            foreach (var inst in instances)
            {
                var placed = false;
                for (var attempt = 0; attempt < 2000 && !placed; attempt++)
                {
                    var vert = rnd.Next(2) == 0;
                    var sx = rnd.Next(Board.GridSize);
                    var sy = rnd.Next(Board.GridSize);
                    if (!ShipPlacementHelper.TryGenerateRectangle(new Coordinate(sx, sy), inst.Length, inst.Width, vert, out var cells) ||
                        cells is null)
                        continue;

                    if (cells.Any(c => occ[c.X, c.Y]))
                        continue;

                    foreach (var c in cells)
                        occ[c.X, c.Y] = true;

                    result.Add(new PlacedShipDto
                    {
                        ShipID = inst.ShipID,
                        StartX = sx,
                        StartY = sy,
                        IsVertical = vert
                    });
                    placed = true;
                }

                if (!placed)
                    throw new InvalidOperationException("Could not auto-place fleet; board may be too crowded.");
            }

            return result;
        }

        public async Task ForfeitAsync(int forfeitingUserId, string reason)
        {
            await EnsureMatchAndFleetAsync();

            int? winnerId = null;
            lock (_gate)
            {
                if (_matchFinished)
                    return;
                if (forfeitingUserId != _player1Id && forfeitingUserId != _player2Id)
                    return;

                /* Opponent wins whether the player leaves during placement or live battle. */
                if (!_battleStarted)
                {
                    winnerId = forfeitingUserId == _player1Id ? _player2Id : _player1Id;
                }
                else if (_game?.Phase == GamePhase.InProgress)
                {
                    winnerId = forfeitingUserId == _player1Id ? _player2Id : _player1Id;
                }
                else
                {
                    return;
                }
            }

            if (winnerId is not int w)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
            var userSvc = scope.ServiceProvider.GetRequiredService<IUserServices>();

            string finishReason;
            if (string.Equals(reason, "AbandonShip", StringComparison.OrdinalIgnoreCase))
            {
                finishReason = await BuildAbandonShipFinishReasonAsync(userSvc, forfeitingUserId, w);
            }
            else if (reason.Contains("AFK", StringComparison.Ordinal))
            {
                finishReason = await BuildOpponentWinsAfterForfeitAsync(
                    userSvc,
                    forfeitingUserId,
                    w,
                    "forfeited due to AFK (three consecutive shot timeouts).");
            }
            else if (reason.Contains("Disconnected", StringComparison.OrdinalIgnoreCase))
            {
                finishReason = await BuildOpponentWinsAfterForfeitAsync(
                    userSvc,
                    forfeitingUserId,
                    w,
                    "forfeited after exceeding the disconnect grace period (2 minutes with no reconnect).");
            }
            else if (string.Equals(reason, "Forfeit", StringComparison.OrdinalIgnoreCase))
            {
                finishReason = await BuildOpponentWinsAfterForfeitAsync(
                    userSvc,
                    forfeitingUserId,
                    w,
                    "forfeited.");
            }
            else
            {
                finishReason = reason;
            }

            List<(int shooterId, int moveNumber, int x, int y, bool hit)> snapshot;
            lock (_gate)
            {
                snapshot = _recordedMoves.ToList();
            }

            var resp = await matchSvc.PersistCompletedBattleAsync(_matchId, w, snapshot, finishReason);
            if (!resp.Success)
            {
                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId))
                    .SendAsync("MatchPersistFailed", resp.Message ?? "Could not save forfeit.");
                return;
            }

            lock (_gate)
            {
                _matchPersisted = true;
                _matchFinished = true;
            }

            StopMatchInfrastructureLocked();

            var (p1Display, p2Display) = await ResolvePlayerDisplayNamesAsync(userSvc, _player1Id, _player2Id);

            var msg = new MatchEndedMessage
            {
                WinnerUserId = w,
                Player1Id = _player1Id,
                Player2Id = _player2Id,
                Player1UserName = p1Display,
                Player2UserName = p2Display,
                Player1RatingChange = resp.Player1RatingChange,
                Player2RatingChange = resp.Player2RatingChange,
                PostMatchDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
                EndReason = finishReason
            };

            await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("MatchEnded", msg);
        }

        private static async Task<string> BuildAbandonShipFinishReasonAsync(
            IUserServices users,
            int forfeitingUserId,
            int winnerUserId)
        {
            var forfeiter = await users.GetByIdAsync(forfeitingUserId);
            var winner = await users.GetByIdAsync(winnerUserId);
            //aaa var loserName = FormatUserDisplayName(forfeiter, forfeitingUserId);
            //aaa var winnerName = FormatUserDisplayName(winner, winnerUserId);
            return $"PlayerID {forfeitingUserId} abandoned ship. PlayerID {winnerUserId} won.";
        }

        private static async Task<string> BuildOpponentWinsAfterForfeitAsync(
            IUserServices users,
            int forfeitingUserId,
            int winnerUserId,
            string midSentence)
        {
            var forfeiter = await users.GetByIdAsync(forfeitingUserId);
            var winner = await users.GetByIdAsync(winnerUserId);
            //aaa var loserName = FormatUserDisplayName(forfeiter, forfeitingUserId);
            //aaa var winnerName = FormatUserDisplayName(winner, winnerUserId);
            return $"PlayerID {forfeitingUserId} {midSentence} PlayerID {winnerUserId} won.";
        }

        private static async Task<string> BuildStandardBattleVictoryReasonAsync(
            IUserServices users,
            int winnerUserId,
            int loserUserId)
        {
            var w = await users.GetByIdAsync(winnerUserId);
            var l = await users.GetByIdAsync(loserUserId);
            //aaa var winnerName = FormatUserDisplayName(w, winnerUserId);
            //aaa var loserName = FormatUserDisplayName(l, loserUserId);
            return $"PlayerID {winnerUserId} won - all of PlayerID {loserUserId}'s ships were destroyed.";
        }

        private static async Task<(string Player1UserName, string Player2UserName)> ResolvePlayerDisplayNamesAsync(
            IUserServices userSvc,
            int player1Id,
            int player2Id)
        {
            var u1 = await userSvc.GetByIdAsync(player1Id);
            var u2 = await userSvc.GetByIdAsync(player2Id);
            return (FormatUserDisplayName(u1, player1Id), FormatUserDisplayName(u2, player2Id));
        }

        private static string FormatUserDisplayName(UserResponseDto? user, int userId)
        {
            if (user is null)
                return $"user #{userId}";
            if (!string.IsNullOrWhiteSpace(user.UserName))
                return user.UserName;
            return $"user #{userId}";
        }

        private void StopMatchInfrastructureLocked()
        {
            _placementTimerCts?.Cancel();
            _placementTimerCts?.Dispose();
            _placementTimerCts = null;
            _shotClockLoopCts?.Cancel();
            _shotClockLoopCts?.Dispose();
            _shotClockLoopCts = null;
            _disconnectGraceCts?.Cancel();
            _disconnectGraceCts?.Dispose();
            _disconnectGraceCts = null;
            _disconnectGraceUserId = null;
        }

        private async Task FanoutFleetLayoutsAsync(IReadOnlyList<ShipType> fleet)
        {
            var byId = fleet.ToDictionary(s => s.ShipID);
            Dictionary<int, string> conns;
            lock (_gate)
            {
                conns = new Dictionary<int, string>(_userConnections);
            }

            foreach (var (uid, conn) in conns)
            {
                List<PlacedShipDto> list = uid == _player1Id ? _p1FinalPlacements : _p2FinalPlacements;
                var cells = ExpandPlacementCellsToShipTypeIds(list, byId);
                await _hubContext.Clients.Client(conn).SendAsync("FleetPlacementSync", new { cells, placements = list });
            }
        }

        private static List<object> ExpandPlacementCellsToShipTypeIds(List<PlacedShipDto> list, Dictionary<int, ShipType> byId)
        {
            var cells = new List<object>();
            foreach (var p in list)
            {
                if (!byId.TryGetValue(p.ShipID, out var st))
                    continue;
                if (!ShipPlacementHelper.TryGenerateRectangle(
                        new Coordinate(p.StartX, p.StartY),
                        st.Length,
                        st.Width,
                        p.IsVertical,
                        out var coords) || coords is null)
                    continue;

                foreach (var c in coords)
                    cells.Add(new { x = c.X, y = c.Y, shipTypeId = p.ShipID });
            }

            return cells;
        }

        private async Task FanoutBattleSnapshotsAsync(GameState game)
        {
            Dictionary<int, string> conns;
            lock (_gate)
            {
                conns = new Dictionary<int, string>(_userConnections);
            }

            foreach (var (uid, conn) in conns)
                await SendBattleSnapshotToClientAsync(game, uid, conn);
        }

        private async Task SendBattleSnapshotToClientAsync(GameState game, int userId, string connectionId)
        {
            List<object> ownFleet;
            List<PlacedShipDto> placements;
            List<object> outgoing;
            List<object> incoming;
            DateTimeOffset deadline;
            lock (_gate)
            {
                var byId = _fleet!.ToDictionary(s => s.ShipID);
                var list = userId == _player1Id ? _p1FinalPlacements : _p2FinalPlacements;
                placements = list.ToList();
                ownFleet = ExpandPlacementCellsToShipTypeIds(list, byId);
                outgoing = new List<object>();
                incoming = new List<object>();
                foreach (var m in _recordedMoves)
                {
                    var intel = new
                    {
                        x = m.x,
                        y = m.y,
                        result = m.shooterId == userId
                            ? IntelResultForOutgoing(game, userId, m.x, m.y, m.hit)
                            : IntelResultForIncoming(game, userId, m.x, m.y, m.hit)
                    };
                    if (m.shooterId == userId)
                        outgoing.Add(intel);
                    else
                        incoming.Add(intel);
                }

                deadline = _shotClockDeadlineUtc;
            }

            await _hubContext.Clients.Client(connectionId).SendAsync("YourBattleSnapshot", new
            {
                player1Id = game.Player1Id,
                player2Id = game.Player2Id,
                currentTurnPlayerId = game.CurrentTurnPlayerId,
                phase = game.Phase.ToString(),
                ownFleet,
                placements,
                myOutgoing = outgoing,
                incomingOnOwn = incoming,
                shotClockDeadlineUtc = deadline
            });
        }

        private void StartShotClockLoop()
        {
            lock (_gate)
            {
                if (_shotClockLoopCts is not null || _game is null || _matchFinished)
                    return;
                _shotClockDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(15);
                _shotClockLoopCts = new CancellationTokenSource();
                _ = ShotClockLoopAsync(_shotClockLoopCts.Token);
            }
        }

        private async Task ShotClockLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(200, ct);
                    await TryProcessShotClockExpiryAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        private async Task TryProcessShotClockExpiryAsync()
        {
            int guilty;
            Coordinate coord;
            lock (_gate)
            {
                if (_matchFinished || !_battleStarted || _game is null || _game.Phase != GamePhase.InProgress)
                    return;
                if (DateTimeOffset.UtcNow < _shotClockDeadlineUtc)
                    return;
                if (_processingForcedShotExpiry)
                    return;
                guilty = _game.CurrentTurnPlayerId;
                if (!_game.TryPickForcedMissOpenWater(Random.Shared, out coord))
                {
                    _shotClockDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(15);
                    return;
                }

                _processingForcedShotExpiry = true;
            }

            try
            {
                await ApplyShotAndBroadcastAsync(guilty, coord.X, coord.Y, forcedTimeout: true, callerConnectionId: null);
            }
            finally
            {
                lock (_gate)
                    _processingForcedShotExpiry = false;
            }
        }

        private void BumpShotClockLocked()
        {
            _shotClockDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(15);
        }

        private async Task<ShotResult?> ApplyShotAndBroadcastAsync(int userId, int targetX, int targetY, bool forcedTimeout, string? callerConnectionId)
        {
            InvalidOperationException? fireEx = null;
            ShotResult result = default;
            var shouldRejectDuplicate = false;
            var shouldBroadcast = false;
            var broadcastTurn = 0;
            var broadcastPhase = "";
            int? winnerId = null;

            lock (_gate)
            {
                if (_matchFinished)
                    goto Exit;

                var g = _game;
                if (g is null || g.Phase != GamePhase.InProgress)
                    goto Exit;

                try
                {
                    result = g.FireShot(userId, new Coordinate(targetX, targetY));
                }
                catch (InvalidOperationException ex)
                {
                    fireEx = ex;
                    goto Exit;
                }

                if (result == ShotResult.AlreadyTargeted)
                {
                    shouldRejectDuplicate = true;
                    goto Exit;
                }

                var n = _recordedMoves.Count + 1;
                var isHit = result is ShotResult.Hit or ShotResult.Win;
                _recordedMoves.Add((userId, n, targetX, targetY, isHit));
                broadcastTurn = g.CurrentTurnPlayerId;
                broadcastPhase = g.Phase.ToString();
                shouldBroadcast = true;
                BumpShotClockLocked();

                // Voluntary shot clears this player's consecutive forced-timeout count only.
                if (!forcedTimeout)
                    _afkTimeoutStreak[userId] = 0;
                else
                    _afkTimeoutStreak[userId] = _afkTimeoutStreak.GetValueOrDefault(userId) + 1;

                if (result == ShotResult.Win)
                    winnerId = g.WinnerId;

            Exit:;
            }

            if (fireEx is not null)
            {
                if (!string.IsNullOrEmpty(callerConnectionId))
                    await _hubContext.Clients.Client(callerConnectionId).SendAsync("ShotRejected", fireEx.Message);
                return null;
            }

            if (shouldRejectDuplicate)
            {
                if (!string.IsNullOrEmpty(callerConnectionId))
                    await _hubContext.Clients.Client(callerConnectionId).SendAsync("ShotRejected", "That cell was already targeted. Pick another.");
                return result;
            }

            if (!shouldBroadcast)
                return null;

            DateTimeOffset shotClockDeadlineUtc;
            lock (_gate)
                shotClockDeadlineUtc = _shotClockDeadlineUtc;

            string clientResult;
            List<object>? sunkCells = null;
            lock (_gate)
            {
                var g = _game;
                clientResult = g is null
                    ? result.ToString()
                    : ToClientShotResult(result, g, userId, targetX, targetY);
                if (g is not null)
                    sunkCells = TryBuildSunkCellsPayload(g, userId, targetX, targetY, clientResult);
            }

            await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("ShotFired", new
            {
                shooterId = userId,
                x = targetX,
                y = targetY,
                result = clientResult,
                sunkCells,
                currentTurnPlayerId = broadcastTurn,
                phase = broadcastPhase,
                forcedTimeout,
                shotClockDeadlineUtc
            });

            if (forcedTimeout)
            {
                int streak;
                lock (_gate)
                {
                    streak = _afkTimeoutStreak.GetValueOrDefault(userId);
                }

                if (streak >= 3)
                {
                    await ForfeitAsync(userId, "AFK - three shot timeouts.");
                    return result;
                }
            }

            if (result == ShotResult.Win && winnerId is int w)
            {
                await FinalizeMatchAfterWinAsync(w);
                return result;
            }

            GameState? g2;
            lock (_gate)
            {
                g2 = _game;
            }

            if (g2 is not null)
                await FanoutBattleSnapshotsAsync(g2);

            return result;
        }

        private async Task RunDisconnectGraceAsync(int disconnectedUserId, CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), ct);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            lock (_gate)
            {
                if (_matchFinished || _disconnectGraceUserId != disconnectedUserId)
                    return;
                if (_userConnections.ContainsKey(disconnectedUserId))
                    return;
                if (!_userConnections.ContainsKey(disconnectedUserId == _player1Id ? _player2Id : _player1Id))
                    return;
            }

            await ForfeitAsync(disconnectedUserId, "Disconnected over 2 minutes.");
        }

        private async Task EnsureMatchAndFleetAsync()
        {
            if (_fleet is not null)
                return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BattleGridDbContext>();
            var match = await db.Match.AsNoTracking().FirstOrDefaultAsync(m => m.MatchID == _matchId)
                        ?? throw new InvalidOperationException($"Match {_matchId} not found.");

            _player1Id = match.Player1ID;
            _player2Id = match.Player2ID;

            _fleet = await db.ShipType.AsNoTracking().OrderBy(s => s.ShipID).ToListAsync();
            _shipsPerPlayer = _fleet.Sum(s => s.MaxPerPlayer);

            await TryRestoreBattleFromDatabaseAsync(db, match);
        }

        /// <summary>
        /// After an API restart, only <see cref="MatchStatus.InProgress"/> matches are resumed from DB.
        /// <see cref="MatchStatus.Loading"/> / <see cref="MatchStatus.PlacingShips"/> use the normal placement flow (no rows yet).
        /// </summary>
        private async Task TryRestoreBattleFromDatabaseAsync(BattleGridDbContext db, Match matchRow)
        {
            if (_fleet is null || matchRow.Status != MatchStatus.InProgress)
                return;

            var placements = await db.ShipPlacement.AsNoTracking()
                .Where(p => p.MatchID == _matchId)
                .ToListAsync();

            if (placements.Count == 0)
                return;

            var requiredPerPlayer = _shipsPerPlayer;
            var p1Rows = placements.Where(p => p.PlayerID == _player1Id).ToList();
            var p2Rows = placements.Where(p => p.PlayerID == _player2Id).ToList();
            if (p1Rows.Count < requiredPerPlayer || p2Rows.Count < requiredPerPlayer)
                return;

            var p1Dtos = p1Rows.Select(r => new PlacedShipDto
            {
                ShipID = r.ShipID,
                StartX = r.StartX,
                StartY = r.StartY,
                IsVertical = r.IsVertical
            }).ToList();
            var p2Dtos = p2Rows.Select(r => new PlacedShipDto
            {
                ShipID = r.ShipID,
                StartX = r.StartX,
                StartY = r.StartY,
                IsVertical = r.IsVertical
            }).ToList();

            var fleetCopy = _fleet.ToList();
            GameState restoredGame;
            lock (_gate)
            {
                if (_battleStarted)
                    return;

                restoredGame = new GameState(_player1Id, _player2Id, _shipsPerPlayer);
                ApplyPlacementsToGameState(restoredGame, _player1Id, p1Dtos, fleetCopy);
                ApplyPlacementsToGameState(restoredGame, _player2Id, p2Dtos, fleetCopy);

                _game = restoredGame;
                _battleStarted = true;
                _placementBroadcastStarted = true;
                _p1FinalPlacements.Clear();
                _p1FinalPlacements.AddRange(p1Dtos);
                _p2FinalPlacements.Clear();
                _p2FinalPlacements.AddRange(p2Dtos);
                _pendingRestoredBattleBroadcast = true;
            }
        }

        public async Task<ShotResult?> FireShotAsync(int userId, int targetX, int targetY, string callerConnectionId)
        {
            lock (_gate)
            {
                if (_matchFinished)
                    return null;
            }

            if (targetX < 0 || targetY < 0 || targetX >= Board.GridSize || targetY >= Board.GridSize)
            {
                await _hubContext.Clients.Client(callerConnectionId).SendAsync("ShotRejected", "Coordinates out of bounds.");
                return null;
            }

            return await ApplyShotAndBroadcastAsync(userId, targetX, targetY, forcedTimeout: false, callerConnectionId);
        }

        private async Task FinalizeMatchBothPlayersDisconnectedAsync()
        {
            lock (_gate)
            {
                if (_matchFinished)
                    return;
            }

            _disconnectGraceCts?.Cancel();
            _disconnectGraceCts?.Dispose();
            _disconnectGraceCts = null;
            _disconnectGraceUserId = null;

            try
            {
                await EnsureMatchAndFleetAsync();
                await using var scope = _scopeFactory.CreateAsyncScope();
                var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
                const string reason =
                    "Match abandoned - both players disconnected simultaneously; no winner determined.";
                var resp = await matchSvc.PersistAbandonedMatchAsync(_matchId, reason);
                if (!resp.Success)
                    return;

                lock (_gate)
                {
                    _matchFinished = true;
                }

                StopMatchInfrastructureLocked();
            }
            catch
            {
                // Clients are already offline; persistence is best-effort only here.
            }
        }

        private async Task FinalizeMatchAfterWinAsync(int winnerUserId)
        {
            List<(int shooterId, int moveNumber, int x, int y, bool hit)> snapshot;
            lock (_gate)
            {
                if (_matchPersisted)
                    return;
                snapshot = _recordedMoves.ToList();
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var matchSvc = scope.ServiceProvider.GetRequiredService<IMatchServices>();
                var userSvc = scope.ServiceProvider.GetRequiredService<IUserServices>();
                var loserUserId = winnerUserId == _player1Id ? _player2Id : _player1Id;
                var finishReason = await BuildStandardBattleVictoryReasonAsync(userSvc, winnerUserId, loserUserId);
                var resp = await matchSvc.PersistCompletedBattleAsync(_matchId, winnerUserId, snapshot, finishReason);
                if (!resp.Success)
                {
                    await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId))
                        .SendAsync("MatchPersistFailed", resp.Message ?? "Could not save match results.");
                    return;
                }

                lock (_gate)
                {
                    _matchPersisted = true;
                    _matchFinished = true;
                }

                StopMatchInfrastructureLocked();

                var (p1Display, p2Display) = await ResolvePlayerDisplayNamesAsync(userSvc, _player1Id, _player2Id);

                var msg = new MatchEndedMessage
                {
                    WinnerUserId = winnerUserId,
                    Player1Id = _player1Id,
                    Player2Id = _player2Id,
                    Player1UserName = p1Display,
                    Player2UserName = p2Display,
                    Player1RatingChange = resp.Player1RatingChange,
                    Player2RatingChange = resp.Player2RatingChange,
                    PostMatchDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30),
                    EndReason = finishReason
                };

                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId)).SendAsync("MatchEnded", msg);
            }
            catch (Exception ex)
            {
                await _hubContext.Clients.Group(InMemoryMatchGameService.GroupName(_matchId))
                    .SendAsync("MatchPersistFailed", ex.Message);
            }
        }

        /// <summary>
        /// Maps domain <see cref="ShotResult.Hit"/> to client <c>Sunk</c> when <see cref="Board.IsShipSunk"/> ran on that salvo.
        /// </summary>
        static string ToClientShotResult(ShotResult result, GameState game, int shooterId, int x, int y)
        {
            if (result == ShotResult.Miss)
                return "Miss";
            if (result == ShotResult.Win)
                return "Win";

            var opponentId = shooterId == game.Player1Id ? game.Player2Id : game.Player1Id;
            var opponentBoard = game.GetPlayerBoard(opponentId);
            if (result == ShotResult.Hit && opponentBoard.GetCellState(x, y) == CellState.Sunk)
                return "Sunk";

            return result.ToString();
        }

        static string IntelResultForOutgoing(GameState game, int viewerUserId, int x, int y, bool hit)
        {
            if (!hit)
                return "Miss";

            var opponentId = viewerUserId == game.Player1Id ? game.Player2Id : game.Player1Id;
            return game.GetPlayerBoard(opponentId).GetCellState(x, y) == CellState.Sunk ? "Sunk" : "Hit";
        }

        static string IntelResultForIncoming(GameState game, int defenderUserId, int x, int y, bool hit)
        {
            if (!hit)
                return "Miss";

            return game.GetPlayerBoard(defenderUserId).GetCellState(x, y) == CellState.Sunk ? "Sunk" : "Hit";
        }

        static List<object>? TryBuildSunkCellsPayload(GameState game, int shooterId, int x, int y, string clientResult)
        {
            if (clientResult is not ("Sunk" or "Win"))
                return null;

            var opponentId = shooterId == game.Player1Id ? game.Player2Id : game.Player1Id;
            var board = game.GetPlayerBoard(opponentId);
            if (!board.TryGetShipIdAt(x, y, out var shipId))
                return null;

            return board.GetCoordinatesForShip(shipId)
                .Select(c => (object)new { x = c.X, y = c.Y })
                .ToList();
        }
    }
}