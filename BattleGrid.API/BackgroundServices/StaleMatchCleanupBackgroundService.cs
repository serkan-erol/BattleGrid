using BattleGrid.Application.Interfaces;

namespace BattleGrid.API.BackgroundServices;

/// <summary>
/// Periodically marks long-running non-terminal matches as <see cref="BattleGrid.Domain.Enums.MatchStatus.Abandoned"/>
/// So they do not stay stuck in Loading / PlacingShips / InProgress after crashes or disconnects.
/// </summary>
public sealed class StaleMatchCleanupBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);    // Runs every 5 minutes
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromHours(1);    // Marks games that started at least an hour ago and still not finished as Abandoned
    private readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);           // Background service starts 2 mins after the server starts running

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleMatchCleanupBackgroundService> _logger;

    public StaleMatchCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleMatchCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        // Initial delay to allow the application to fully start before running background services
        await Task.Delay(StartupDelay, stoppingToken);

        try
        {
            await RunCleanupAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await RunCleanupAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stale match cleanup tick failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutdown
        }
    }

    async Task RunCleanupAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var matches = scope.ServiceProvider.GetRequiredService<IMatchServices>();
        var abandoned = await matches.AbandonStaleMatchesAsync(StaleThreshold, stoppingToken);
        if (abandoned > 0)
            _logger.LogInformation(
                "Stale match cleanup abandoned {Count} match(es) in Loading/PlacingShips/InProgress older than {Threshold}.",
                abandoned,
                StaleThreshold);
    }
}