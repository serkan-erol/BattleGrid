namespace BattleGrid.Tests.Integration;

/// <summary>
/// Tracks integration-test users and fixtures for guaranteed teardown after each test.
/// </summary>
internal sealed class IntegrationTestDataTracker
{
    private readonly HashSet<string> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _matchIds = new();
    private readonly object _gate = new();

    public void TrackEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return;

        lock (_gate)
            _emails.Add(email.Trim());
    }

    public void TrackSession(TestUserSession session) => TrackEmail(session.Email);

    public void TrackSessions(IEnumerable<TestUserSession> sessions)
    {
        foreach (var session in sessions)
            TrackSession(session);
    }

    public void TrackMatch(int matchId)
    {
        if (matchId <= 0)
            return;

        lock (_gate)
            _matchIds.Add(matchId);
    }

    public async Task CleanupAllAsync(BattleGridApiFactory factory)
    {
        List<int> matchIds;
        List<string> emails;
        lock (_gate)
        {
            matchIds = _matchIds.ToList();
            emails = _emails.ToList();
            _matchIds.Clear();
            _emails.Clear();
        }

        foreach (var matchId in matchIds)
            await ApiIntegrationTestHelper.DeleteMatchFixtureAsync(factory, matchId);

        foreach (var email in emails)
            await ApiIntegrationTestHelper.CleanupTestUserAsync(factory, email);

        await ApiIntegrationTestHelper.CleanupOrphanedIntegrationUsersAsync(factory);
    }
}