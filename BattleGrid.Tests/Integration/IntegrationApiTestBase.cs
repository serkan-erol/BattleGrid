namespace BattleGrid.Tests.Integration;

/// <summary>
/// Base for API integration tests. xUnit creates a new instance per test;
/// <see cref="DisposeAsync"/> runs after every test and removes tracked DB rows.
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationApiTestBase : IAsyncLifetime
{
    private readonly IntegrationTestDataTracker _tracker = new();

    protected IntegrationApiTestBase(BattleGridApiFactory factory) => Factory = factory;

    protected BattleGridApiFactory Factory { get; }

    protected HttpClient Client { get; private set; } = null!;

    protected bool DatabaseAvailable { get; private set; }

    public virtual async Task InitializeAsync()
    {
        Client = Factory.CreateClient();
        DatabaseAvailable = await Factory.CanConnectToDatabaseAsync();
        ApiIntegrationTestHelper.BindTracker(_tracker);
    }

    public virtual async Task DisposeAsync()
    {
        try
        {
            if (DatabaseAvailable)
                await _tracker.CleanupAllAsync(Factory);
        }
        finally
        {
            ApiIntegrationTestHelper.BindTracker(null);
        }
    }
}