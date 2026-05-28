using BattleGrid.Contracts;
using BattleGrid.Contracts.ResponseDtos;
using Microsoft.AspNetCore.Http;

namespace BattleGrid.Web.Extensions;

public static class AuthCookieExtensions
{
    public static void AppendAuthCookies(
        this HttpResponse response,
        string accessToken,
        string refreshToken,
        DateTimeOffset accessExpires,
        DateTimeOffset refreshExpires,
        bool isHttps)
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
            accessToken,
            new CookieOptions
            {
                HttpOnly = cookieBase.HttpOnly,
                Secure = cookieBase.Secure,
                SameSite = cookieBase.SameSite,
                Path = cookieBase.Path,
                Expires = accessExpires
            });

        response.Cookies.Append(
            AuthCookieNames.RefreshToken,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = cookieBase.HttpOnly,
                Secure = cookieBase.Secure,
                SameSite = cookieBase.SameSite,
                Path = cookieBase.Path,
                Expires = refreshExpires
            });
    }

    public static void AppendAuthCookies(this HttpResponse response, LoginResponseDto tokens, bool isHttps) =>
        response.AppendAuthCookies(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ATExpiresAt,
            tokens.RTExpiresAt,
            isHttps);

    public static void ClearAuthCookies(this HttpResponse response)
    {
        var options = new CookieOptions { Path = "/" };
        response.Cookies.Delete(AuthCookieNames.AccessToken, options);
        response.Cookies.Delete(AuthCookieNames.RefreshToken, options);
    }
}