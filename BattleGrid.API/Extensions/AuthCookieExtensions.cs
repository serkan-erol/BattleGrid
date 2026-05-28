using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Http;

namespace BattleGrid.API.Extensions;

public static class AuthCookieExtensions
{
    public static string? ResolveRefreshToken(this HttpRequest request, string? bodyToken)
    {
        if (!string.IsNullOrWhiteSpace(bodyToken))
            return bodyToken;

        request.Cookies.TryGetValue(AuthCookieNames.RefreshToken, out var cookieToken);
        return cookieToken;
    }

    public static void AppendAuthCookies(this HttpResponse response, LoginResponseDto tokens, bool isHttps)
    {
        var cookieBase = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.None,
            Path = "/"
        };

        response.Cookies.Append(
            AuthCookieNames.AccessToken,
            tokens.AccessToken,
            new CookieOptions
            {
                HttpOnly = cookieBase.HttpOnly,
                Secure = cookieBase.Secure,
                SameSite = cookieBase.SameSite,
                Path = cookieBase.Path,
                Expires = tokens.ATExpiresAt
            });

        response.Cookies.Append(
            AuthCookieNames.RefreshToken,
            tokens.RefreshToken,
            new CookieOptions
            {
                HttpOnly = cookieBase.HttpOnly,
                Secure = cookieBase.Secure,
                SameSite = cookieBase.SameSite,
                Path = cookieBase.Path,
                Expires = tokens.RTExpiresAt
            });
    }

    public static void ClearAuthCookies(this HttpResponse response)
    {
        var options = new CookieOptions { Path = "/" };
        response.Cookies.Delete(AuthCookieNames.AccessToken, options);
        response.Cookies.Delete(AuthCookieNames.RefreshToken, options);
    }
}