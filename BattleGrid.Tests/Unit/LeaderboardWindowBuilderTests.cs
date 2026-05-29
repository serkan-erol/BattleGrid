using BattleGrid.Application.Helpers;
using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Tests.Unit;

public sealed class LeaderboardWindowBuilderTests
{
    private static List<LeaderboardEntryDto> CreateRankedList(int count, int startUserId = 1)
    {
        return Enumerable.Range(0, count)
            .Select(i => new LeaderboardEntryDto
            {
                UserId = startUserId + i,
                Rank = i + 1,
                UserName = $"player{startUserId + i}",
                Rating = 2000 - i,
                MatchesPlayed = 10,
                MatchesWon = 5,
                WinRate = 50,
                SeasonNo = 1
            })
            .ToList();
    }

    [Fact]
    public void BuildDisplayList_Anonymous_ReturnsTop100Only()
    {
        var ranked = CreateRankedList(250);

        var result = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId: null);

        Assert.Equal(LeaderboardLimits.TopRankCount, result.Count);
        Assert.Equal(1, result[0].Rank);
        Assert.Equal(100, result[^1].Rank);
    }

    [Fact]
    public void BuildDisplayList_ViewerInTop100_ReturnsTop100Only()
    {
        var ranked = CreateRankedList(250);

        var result = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId: 50);

        Assert.Equal(LeaderboardLimits.TopRankCount, result.Count);
        Assert.Contains(result, e => e.UserId == 50);
    }

    [Fact]
    public void BuildDisplayList_ViewerOutsideTop100_IncludesContextWindow()
    {
        var ranked = CreateRankedList(250);
        const int viewerUserId = 150;

        var result = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId);

        Assert.Equal(LeaderboardLimits.TopRankCount, result.Count(e => e.Rank <= LeaderboardLimits.TopRankCount));
        Assert.Contains(result, e => e.UserId == viewerUserId);

        var viewer = result.Single(e => e.UserId == viewerUserId);
        Assert.Equal(150, viewer.Rank);

        Assert.Contains(result, e => e.Rank == 146);
        Assert.Contains(result, e => e.Rank == 155);
        Assert.DoesNotContain(result, e => e.Rank == 145);
        Assert.DoesNotContain(result, e => e.Rank == 156);
    }

    [Fact]
    public void BuildDisplayList_ViewerNearBottom_IncludesAvailableContextBelow()
    {
        var ranked = CreateRankedList(105);
        const int viewerUserId = 104;

        var result = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId);

        Assert.Contains(result, e => e.UserId == viewerUserId);
        Assert.Contains(result, e => e.Rank == 105);
        Assert.DoesNotContain(result, e => e.Rank > 105);
    }

    [Fact]
    public void BuildDisplayList_ViewerAtEnd_IncludesAvailableContextBelow()
    {
        var ranked = CreateRankedList(105);
        const int viewerUserId = 105;

        var result = LeaderboardWindowBuilder.BuildDisplayList(ranked, viewerUserId);

        Assert.Contains(result, e => e.UserId == viewerUserId);
        Assert.Contains(result, e => e.Rank == 101);
        Assert.DoesNotContain(result, e => e.Rank == 110);
        Assert.Equal(105, result.Max(e => e.Rank));
    }

    [Fact]
    public void MergeTopAndContext_DeduplicatesOverlappingRanks()
    {
        var top = CreateRankedList(100);
        var context = top.Where(e => e.Rank >= 97).ToList();

        var merged = LeaderboardWindowBuilder.MergeTopAndContext(top, context);

        Assert.Equal(100, merged.Count);
        Assert.Equal(Enumerable.Range(1, 100), merged.Select(e => e.Rank));
    }
}