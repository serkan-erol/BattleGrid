namespace BattleGrid.Contracts;

/// <summary>
/// HTTP cookie names for JWT session tokens. Not related to <c>Session.AccessToken</c> / <c>Session.RefreshToken</c> database columns.
/// </summary>
public static class AuthCookieNames
{
    public const string AccessToken = "bg_access_token";
    public const string RefreshToken = "bg_refresh_token";
}
