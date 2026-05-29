using BattleGrid.Application.Helpers;
using BattleGrid.Domain.Entities;

namespace BattleGrid.Tests.Unit;

public sealed class LeaderboardAllTimeBuilderTests
{
    [Fact]
    public void BuildEntry_UsesMaxHighestRating_NotMaxCurrentRating()
    {
        var userId = 42;
        var rows = new[]
        {
            (Stat: new PlayerStat
            {
                UserID = userId,
                SeasonNo = 1,
                Rating = 2100,
                HighestRating = 1800,
                MatchesPlayed = 5,
                MatchesWon = 2
            }, UserName: "captain"),
            (Stat: new PlayerStat
            {
                UserID = userId,
                SeasonNo = 2,
                Rating = 1500,
                HighestRating = 1950,
                MatchesPlayed = 3,
                MatchesWon = 1
            }, UserName: "captain")
        };

        var entry = LeaderboardAllTimeBuilder.BuildEntry(rows);

        Assert.Equal(1950, entry.Rating);
        Assert.Equal(2, entry.SeasonNo);
        Assert.Equal(8, entry.MatchesPlayed);
        Assert.Equal(3, entry.MatchesWon);
    }

    [Fact]
    public void BuildRankedList_OrdersByPeakHighestRating()
    {
        var rows = new[]
        {
            (Stat: new PlayerStat { UserID = 1, SeasonNo = 1, Rating = 2500, HighestRating = 2500, MatchesPlayed = 1, MatchesWon = 1 }, UserName: "a"),
            (Stat: new PlayerStat { UserID = 2, SeasonNo = 1, Rating = 2400, HighestRating = 2600, MatchesPlayed = 1, MatchesWon = 0 }, UserName: "b")
        };

        var ranked = LeaderboardAllTimeBuilder.BuildRankedList(rows);

        Assert.Equal(2, ranked.Count);
        Assert.Equal(2, ranked[0].UserId);
        Assert.Equal(2600, ranked[0].Rating);
        Assert.Equal(1, ranked[1].UserId);
        Assert.Equal(2500, ranked[1].Rating);
    }
}
