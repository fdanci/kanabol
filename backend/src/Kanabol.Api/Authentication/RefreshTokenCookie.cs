using Kanabol.Application.Abstractions;

namespace Kanabol.Api.Authentication;

/// <summary>The refresh token's only way in and out of the API (SPEC section 4.5).</summary>
public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    private const string Path = "/api/auth";

    public static string Read(HttpRequest request) => request.Cookies[Name] ?? "";

    public static void Write(HttpResponse response, NewRefreshToken token) =>
        response.Cookies.Append(Name, token.Value, CreateOptions(token.ExpiresAt));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, CreateOptions(null));

    private static CookieOptions CreateOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
    };
}
