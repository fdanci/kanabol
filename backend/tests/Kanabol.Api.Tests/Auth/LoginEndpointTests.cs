using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Kanabol.Api.Tests.Auth;

public sealed class LoginEndpointTests(ApiFixture api) : AuthApiTest(api)
{
    [Fact]
    public async Task Login_WhenTheCredentialsAreRight_ReturnsTheAccessTokenInTheBody()
    {
        var user = await CreateUser();

        var response = await Login(user.Email);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Login_WhenTheCredentialsAreRight_SetsTheRefreshCookieAsTheSpecAsks()
    {
        var user = await CreateUser();

        var response = await Login(user.Email);

        var cookie = RefreshCookieFrom(response);
        cookie.HttpOnly.Should().BeTrue();
        cookie.Secure.Should().BeTrue();
        cookie.SameSite.Should().Be(SameSiteMode.Strict);
        cookie.Path.ToString().Should().Be("/api/auth");
        cookie.Expires.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_WhenTheCredentialsAreRight_KeepsTheRefreshTokenOutOfTheBody()
    {
        var user = await CreateUser();

        var response = await Login(user.Email);

        (await response.Content.ReadAsStringAsync()).Should().NotContain(RefreshTokenFrom(response));
    }

    [Fact]
    public async Task Login_WhenTheCredentialsAreRight_StoresTheCookiesValueOnlyAsAHash()
    {
        var user = await CreateUser();

        var response = await Login(user.Email);

        await using var db = Api.Postgres.CreateContext();
        (await db.RefreshTokens.SingleAsync()).TokenHash.Should().NotBe(RefreshTokenFrom(response));
    }

    [Fact]
    public async Task Login_WhenThePasswordIsWrongOrTheEmailUnknown_AnswersTheSame()
    {
        var user = await CreateUser();

        var wrongPassword = await Login(user.Email, "alta parola complet");
        var unknownEmail = await Login("nobody@example.com");

        foreach (var response in new[] { wrongPassword, unknownEmail })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
            response.Headers.Contains("Set-Cookie").Should().BeFalse();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be("Auth.InvalidCredentials");
        }
    }

    [Fact]
    public async Task Login_WhenAFieldIsMissing_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email = "ana@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Login_AsTheSeededAdmin_WorksWithTheInitialPasswordAndAsksToChangeIt()
    {
        var response = await Login("admin@kanabol.local", "only-for-these-tests");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }
}
