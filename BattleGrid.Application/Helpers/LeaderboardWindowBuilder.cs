using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;

namespace BattleGrid.Application.Helpers;

public static class LeaderboardWindowBuilder
{
    public static List<LeaderboardEntryDto> BuildDisplayList(
        IReadOnlyList<LeaderboardEntryDto> ranked,
        int? viewerUserId)
    {
        var top = ranked.Take(LeaderboardLimits.TopRankCount).ToList();
        if (viewerUserId is null)
            return top;

        if (top.Any(e => e.UserId == viewerUserId.Value))
            return top;

        var userIndex = ranked.ToList().FindIndex(e => e.UserId == viewerUserId.Value);
        if (userIndex < 0)
            return top;

        var start = Math.Max(0, userIndex - LeaderboardLimits.ViewerRankContextAbove);
        var end = Math.Min(ranked.Count - 1, userIndex + LeaderboardLimits.ViewerRankContextBelow);
        var context = ranked.Skip(start).Take(end - start + 1).ToList();

        return MergeTopAndContext(top, context);
    }

    public static List<LeaderboardEntryDto> MergeTopAndContext(
        IReadOnlyList<LeaderboardEntryDto> top,
        IReadOnlyList<LeaderboardEntryDto> context)
    {
        return top
            .Concat(context)
            .GroupBy(e => e.UserId)
            .Select(g => g.First())
            .OrderBy(e => e.Rank)
            .ToList();
    }

    public static void AssignRanks(IList<LeaderboardEntryDto> entries, int startRank = 1)
    {
        for (var i = 0; i < entries.Count; i++)
            entries[i].Rank = startRank + i;
    }
}