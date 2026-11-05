using System.Net;
using Kanabol.Api.Tests.Support;

namespace Kanabol.Api.Tests.Auth;

public sealed class LogoutEndpointTests(ApiFixture api) : AuthApiTest(api)
{
    [Fact]
    public async Task Logout_WhenTheCookieIsValid_Returns204ClearsItAndEndsTheSession()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        var response = await Logout(RefreshTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var cookie = RefreshCookieFrom(response);
        cookie.Value.ToString().Should().BeEmpty();
        cookie.Path.ToString().Should().Be("/api/auth");
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
        (await Refresh(RefreshTokenFrom(login))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WhenThereIsNoCookie_StillReturns204()
    {
        var response = await Logout(null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
