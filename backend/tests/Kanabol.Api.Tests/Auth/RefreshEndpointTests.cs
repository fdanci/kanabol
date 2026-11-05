using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;

namespace Kanabol.Api.Tests.Auth;

public sealed class RefreshEndpointTests(ApiFixture api) : AuthApiTest(api)
{
    [Fact]
    public async Task Refresh_WhenTheCookieIsValid_ReturnsANewAccessTokenAndCookie()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        var response = await Refresh(RefreshTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        var cookie = RefreshCookieFrom(response);
        cookie.Value.ToString().Should().NotBe(RefreshTokenFrom(login));
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
    }

    [Fact]
    public async Task Refresh_WhenThereIsNoCookie_Returns401()
    {
        var response = await Refresh(null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("Auth.InvalidRefreshToken");
    }

    [Fact]
    public async Task Refresh_WhenAnOldTokenIsReused_Returns401AndClearsTheCookie()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);
        await Refresh(RefreshTokenFrom(login));

        var response = await Refresh(RefreshTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var cookie = RefreshCookieFrom(response);
        cookie.Value.ToString().Should().BeEmpty();
        cookie.Path.ToString().Should().Be("/api/auth");
        cookie.Expires.Should().BeBefore(DateTimeOffset.UtcNow);
    }
}
