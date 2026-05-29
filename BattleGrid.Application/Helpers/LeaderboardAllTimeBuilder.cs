using BattleGrid.Contracts.ResponseDtos;
using BattleGrid.Domain.Entities;

namespace BattleGrid.Application.Helpers;

public static class LeaderboardAllTimeBuilder
{
    public static LeaderboardEntryDto BuildEntry(IEnumerable<(PlayerStat Stat, string UserName)> seasons)
    {
        var rows = seasons.ToList();
        if (rows.Count == 0)
            throw new ArgumentException("At least one season row is required.", nameof(seasons));

        var peakRating = rows.Max(x => x.Stat.HighestRating);
        var peakSeason = rows
            .Where(x => x.Stat.HighestRating == peakRating)
            .OrderByDescending(x => x.Stat.SeasonNo)
            .ThenBy(x => x.Stat.StatID)
            .First();

        var matchesPlayed = rows.Sum(x => x.Stat.MatchesPlayed);
        var matchesWon = rows.Sum(x => x.Stat.MatchesWon);

        return new LeaderboardEntryDto
        {
            UserId = peakSeason.Stat.UserID,
            UserName = peakSeason.UserName,
            Rating = peakRating,
            MatchesPlayed = matchesPlayed,
            MatchesWon = matchesWon,
            WinRate = matchesPlayed > 0
                ? Math.Round((decimal)matchesWon / matchesPlayed * 100, 2)
                : 0,
            SeasonNo = peakSeason.Stat.SeasonNo
        };
    }

    public static List<LeaderboardEntryDto> BuildRankedList(
        IEnumerable<(PlayerStat Stat, string UserName)> seasonRows)
    {
        return seasonRows
            .GroupBy(x => x.Stat.UserID)
            .Select(BuildEntry)
            .OrderByDescending(x => x.Rating)
            .ThenBy(x => x.UserId)
            .ToList();
    }
}
