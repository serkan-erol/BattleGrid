namespace BattleGrid.Tests.Integration;

/// <summary>
/// Shared API factory and serialized execution so integration tests do not race on the same PostgreSQL data.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<BattleGridApiFactory>
{
    public const string Name = "Integration";
}