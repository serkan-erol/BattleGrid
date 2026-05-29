using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace BattleGrid.Web.Services;

/// <summary>
/// Keeps the matchmaking SignalR connection alive while the user browses other pages on the same circuit.
/// </summary>
public sealed class QueueMatchmakingService : IAsyncDisposable
{
    private static readonly TimeSpan UiTickInterval = TimeSpan.FromSeconds(1);

    private readonly AuthStateService _auth;
    private readonly NavigationManager _nav;
    private readonly IConfiguration _config;
    private readonly IJSRuntime _js;
    private readonly ResumableMatchService _resumable;

    private HubConnection? _hub;
    private SynchronizationContext? _uiContext;
    private PeriodicTimer? _uiTimer;
    private CancellationTokenSource? _uiTimerCts;
    private bool _handlersRegistered;
    private bool _authSubscribed;
    private Task? _ensureTask;

    public QueueMatchmakingService(
        AuthStateService auth,
        NavigationManager nav,
        IConfiguration config,
        IJSRuntime js,
        ResumableMatchService resumable)
    {
        _auth = auth;
        _nav = nav;
        _config = config;
        _js = js;
        _resumable = resumable;
    }

    public event Action? OnStateChanged;

    public string Status { get; private set; } = "Connecting…";

    public DateTimeOffset? QueueStartedUtc { get; private set; }

    /// <summary> Matchmaking rating from the server when the queue session starts (cleared when leaving). </summary>
    public int? QueueMatchmakingRating { get; private set; }

    public HubConnectionState? HubState => _hub?.State;

    public bool IsInQueue => _hub is { State: HubConnectionState.Connected };

    public TimeSpan GetQueueElapsed()
    {
        if (QueueStartedUtc is null)
            return TimeSpan.Zero;
        var e = DateTimeOffset.UtcNow - QueueStartedUtc.Value;
        return e < TimeSpan.Zero ? TimeSpan.Zero : e;
    }

    public static string FormatWaitClock(TimeSpan elapsed)
    {
        var total = (int)elapsed.TotalSeconds;
        var m = total / 60;
        var s = total % 60;
        return $"{m}:{s:D2}";
    }

    public Task EnsureStartedAsync() => RunOnUiAsync(EnsureStartedCoreAsync);

    private bool _queueAutoResumeComplete;

    /// <summary> Reconnects the queue hub after a full page reload when sessionStorage still marks the user as queued. </summary>
    public async Task TryResumeFromStoredIntentAsync(CancellationToken cancellationToken = default)
    {
        if (_queueAutoResumeComplete)
            return;

        await _auth.InitializeAsync();
        if (!_auth.IsLoggedIn || string.IsNullOrEmpty(_auth.AccessToken))
        {
            _queueAutoResumeComplete = true;
            return;
        }

        await _resumable.RefreshAsync();
        if (_resumable.HasResumableMatch)
        {
            await TryClearBrowserQueueIntentAsync();
            _queueAutoResumeComplete = true;
            _nav.NavigateTo($"/game/{_resumable.MatchId}");
            return;
        }

        bool hasIntent;
        try
        {
            hasIntent = await TryHasIntentAsync();
        }
        catch
        {
            return;
        }

        if (!hasIntent)
        {
            _queueAutoResumeComplete = true;
            return;
        }

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                await EnsureStartedAsync();
                _queueAutoResumeComplete = true;
                return;
            }
            catch (JSDisconnectedException)
            {
                _queueAutoResumeComplete = true;
                return;
            }
            catch when (attempt < 5)
            {
                await Task.Delay(32 * (attempt + 1), cancellationToken);
            }
        }

        _queueAutoResumeComplete = true;
    }

    public Task LeaveQueueAsync() => RunOnUiAsync(LeaveQueueCoreAsync);

    public async ValueTask DisposeAsync()
    {
        if (_authSubscribed)
        {
            _auth.OnChange -= OnAuthChanged;
            _authSubscribed = false;
        }

        await StopUiTimerAsync();
        var hub = _hub;
        _hub = null;
        _handlersRegistered = false;
        if (hub is not null)
        {
            try
            {
                await Task.Run(async () => await hub.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            }
            catch
            {
                /* circuit teardown / socket already closed */
            }
        }
    }

    void OnAuthChanged()
    {
        _ = RunOnUiAsync(async () =>
        {
            if (!_auth.IsInitialized || !_auth.IsLoggedIn || string.IsNullOrEmpty(_auth.AccessToken))
                await LeaveQueueCoreAsync();
        });
    }

    async Task EnsureStartedCoreAsync()
    {
        if (!_authSubscribed)
        {
            _auth.OnChange += OnAuthChanged;
            _authSubscribed = true;
        }

        if (_ensureTask is not null)
        {
            await _ensureTask;
            return;
        }

        _ensureTask = EnsureStartedOnceAsync();
        try
        {
            await _ensureTask;
        }
        finally
        {
            _ensureTask = null;
        }
    }

    async Task EnsureStartedOnceAsync()
    {
        await _auth.InitializeAsync();
        if (!_auth.IsLoggedIn || string.IsNullOrEmpty(_auth.AccessToken))
            return;

        await _resumable.RefreshAsync();
        if (_resumable.MatchId is int resumableId)
        {
            await TryClearBrowserQueueIntentAsync();
            Status = "You have a match in progress - rejoin to continue.";
            NotifyStateChanged();
            _nav.NavigateTo($"/game/{resumableId}");
            return;
        }

        if (_hub is not null)
        {
            if (_hub.State == HubConnectionState.Disconnected)
                await StartHubCoreAsync();
            return;
        }

        await BuildAndStartHubAsync();
    }

    async Task LeaveQueueCoreAsync()
    {
        await TryClearBrowserQueueIntentAsync();
        var hub = _hub;
        if (hub is { State: HubConnectionState.Connected })
        {
            try
            {
                await hub.InvokeAsync("VoluntaryLeaveQueue");
            }
            catch
            {
                /* still drop connection */
            }
        }

        await StopUiTimerAsync();
        QueueStartedUtc = null;
        QueueMatchmakingRating = null;
        _hub = null;
        _handlersRegistered = false;
        Status = "Disconnected.";
        NotifyStateChanged();

        if (hub is not null)
            await Task.Run(async () => await hub.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
    }

    async Task BuildAndStartHubAsync()
    {
        _hub = ApiHubConnectionFactory.Create(_config, _auth, "/queue");

        if (!_handlersRegistered)
        {
            _hub.On<int>("QueueRejected", async matchId =>
            {
                await RunOnUiAsync(async () =>
                {
                    await _resumable.RefreshAsync();
                    await TryClearBrowserQueueIntentAsync();
                    await StopUiTimerAsync();
                    QueueStartedUtc = null;
                    QueueMatchmakingRating = null;
                    Status = $"You have match #{matchId} in progress. Rejoin it first.";
                    NotifyStateChanged();
                    _nav.NavigateTo($"/game/{matchId}");
                });
            });

            _hub.On<long, int>("QueueJoined", async (unixMs, rating) =>
            {
                await RunOnUiAsync(async () =>
                {
                    QueueStartedUtc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
                    QueueMatchmakingRating = rating;
                    await StartUiTimerAsync();
                    await TryPersistBrowserQueueIntentAsync();
                    Status = "In queue - waiting for opponent…";
                    NotifyStateChanged();
                });
            });

            _hub.Reconnecting += ex =>
            {
                _ = RunOnUiAsync(async () =>
                {
                    Status = "Reconnecting…";
                    await StopUiTimerAsync();
                    QueueStartedUtc = null;
                    QueueMatchmakingRating = null;
                    NotifyStateChanged();
                });
                return Task.CompletedTask;
            };

            _hub.Reconnected += connectionId =>
            {
                _ = RunOnUiAsync(async () =>
                {
                    Status = "In queue - waiting for opponent…";
                    NotifyStateChanged();
                });
                return Task.CompletedTask;
            };

            _hub.Closed += async ex =>
            {
                await RunOnUiAsync(async () =>
                {
                    if (_hub is null)
                        return;
                    await StopUiTimerAsync();
                    QueueStartedUtc = null;
                    QueueMatchmakingRating = null;
                    Status = ex is null ? "Disconnected." : $"Disconnected: {ex.Message}";
                    _hub = null;
                    _handlersRegistered = false;
                    NotifyStateChanged();
                });
            };

            _hub.On<int, int>("Matched", async (matchId, _) =>
            {
                HubConnection? hub = null;
                await RunOnUiAsync(async () =>
                {
                    await StopUiTimerAsync();
                    QueueStartedUtc = null;
                    QueueMatchmakingRating = null;
                    await TryClearBrowserQueueIntentAsync();
                    hub = _hub;
                    _hub = null;
                    _handlersRegistered = false;
                    Status = "Matched.";
                    NotifyStateChanged();
                    try
                    {
                        await _js.InvokeVoidAsync("battleGridSounds.playMatchFound");
                    }
                    catch
                    {
                        /* prerender / autoplay / disconnected */
                    }

                    _nav.NavigateTo($"/game/{matchId}");
                });

                if (hub is not null)
                    await Task.Run(async () => await hub.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            });

            _handlersRegistered = true;
        }

        await StartHubCoreAsync();
    }

    async Task StartHubCoreAsync()
    {
        if (_hub is null)
            return;

        try
        {
            await _hub.StartAsync();
            Status = "In queue - syncing…";
        }
        catch (Exception ex)
        {
            Status = $"Could not connect: {ex.Message}";
            QueueStartedUtc = null;
            QueueMatchmakingRating = null;
            await StopUiTimerAsync();
        }

        NotifyStateChanged();
    }

    async Task StartUiTimerAsync()
    {
        await StopUiTimerAsync();
        _uiTimerCts = new CancellationTokenSource();
        var ct = _uiTimerCts.Token;
        _uiTimer = new PeriodicTimer(UiTickInterval);
        _ = RunUiTimerLoopAsync(ct);
    }

    async Task RunUiTimerLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _uiTimer is not null)
            {
                if (!await _uiTimer.WaitForNextTickAsync(ct))
                    break;
                NotifyStateChanged();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    async Task StopUiTimerAsync()
    {
        try
        {
            _uiTimerCts?.Cancel();
        }
        catch
        {
            /* disposed */
        }

        _uiTimer?.Dispose();
        _uiTimer = null;
        _uiTimerCts?.Dispose();
        _uiTimerCts = null;
        await Task.CompletedTask;
    }

    void NotifyStateChanged() => OnStateChanged?.Invoke();

    async Task TryPersistBrowserQueueIntentAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("battleGridQueueSession.setIntent");
        }
        catch
        {
            /* prerender / storage unavailable */
        }
    }

    async Task TryClearBrowserQueueIntentAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("battleGridQueueSession.clearIntent");
        }
        catch
        {
            /* ignore */
        }
    }

    async Task<bool> TryHasIntentAsync()
    {
        try
        {
            return await _js.InvokeAsync<bool>("battleGridQueueSession.hasIntent");
        }
        catch
        {
            return false;
        }
    }

    Task RunOnUiAsync(Func<Task> work)
    {
        var ctx = _uiContext ??= SynchronizationContext.Current;
        if (ctx is null)
            return work();

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.Post(_ => RunPostedAsync(work, tcs), null);
        return tcs.Task;
    }

    static async void RunPostedAsync(Func<Task> work, TaskCompletionSource tcs)
    {
        try
        {
            await work();
            tcs.TrySetResult();
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }
}