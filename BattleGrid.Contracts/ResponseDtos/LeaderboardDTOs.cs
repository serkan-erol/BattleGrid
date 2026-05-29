namespace BattleGrid.Contracts.ResponseDtos;

public class LeaderboardEntryDto
{
    public int UserId { get; set; }
    public int Rank { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Rating { get; set; }
    public int MatchesPlayed { get; set; }
    public int MatchesWon { get; set; }
    public decimal WinRate { get; set; }

    public int SeasonNo { get; set; }
}

public class LeaderboardListResponseDto
{
    public IReadOnlyList<LeaderboardEntryDto> Entries { get; set; } = [];

    public int PageSize { get; set; } = LeaderboardLimits.PageSize;
}