using BattleGrid.Domain.Enums;

namespace BattleGrid.Contracts.ResponseDtos;

public sealed class MatchHistoryEntryResponseDto
{
    public int MatchId { get; init; }
    public string OpponentUserName { get; init; } = string.Empty;

    /// <summary><c>true</c> = win, <c>false</c> = loss, <c>null</c> = no winner (abandoned).</summary>
    public bool? PlayerWon { get; init; }

    public int RatingChange { get; init; }

    /// <summary>Domain <see cref="BattleGrid.Domain.Enums.MatchStatus"/> value.</summary>
    public int Status { get; init; }

    /// <summary>Domain <see cref="BattleGrid.Domain.Enums.MatchHistoryEndKind"/> value.</summary>
    public MatchHistoryEndKind EndKind { get; init; }

    public DateTimeOffset? FinishedAt { get; init; }
}

public sealed class MatchHistoryResponseDto
{
    public IReadOnlyList<MatchHistoryEntryResponseDto> Matches { get; init; } = Array.Empty<MatchHistoryEntryResponseDto>();
}
