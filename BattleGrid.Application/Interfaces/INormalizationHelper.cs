namespace BattleGrid.Application.Interfaces;

public interface INormalizationHelper
{
    Task<string> NormalizeLoginInfoAsync(string loginInfo);
}