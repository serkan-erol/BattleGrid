namespace BattleGrid.Domain.Enums;

/// <summary>How the match ended from the requesting player's perspective.</summary>
public enum MatchHistoryEndKind
{
    // Decisive win/loss from normal gameplay (all ships destroyed)
    NormalBattle = 0,

    // Opponent left, forfeited, disconnected, went AFK, or abandoned ship
    OpponentLeft = 1,

    // The requesting player left, forfeited, disconnected, went AFK, or abandoned ship
    SelfLeft = 2,

    // Match status is Abandoned(no recorded winner)
    Abandoned = 3
}