using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Auth;

/// <summary>
/// Whole sessions over real HTTP, with a client that keeps cookies the way a browser does: it stores
/// what the API sets and sends it back by itself, so nothing here hands a token over by hand.
/// </summary>
public sealed class AuthFlowTests(ApiFixture api) : AuthApiTest(api)
{
    private const int FailuresBeforeTheLock = 10;
    private const string NewPassword = "alta parola complet noua";
    private const string WrongPassword = "nu este parola buna";

    private readonly HttpClient _browser = api.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    public override async Task DisposeAsync()
    {
        _browser.Dispose();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task Session_FromLoginToLogout_WorksAsTheBrowserUsesIt()
    {
        var user = await CreateUser();

        var login = await LoginInBrowser(user.Email, Password);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMe(await AccessTokenFrom(login))).StatusCode.Should().Be(HttpStatusCode.OK);

        var refresh = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMe(await AccessTokenFrom(refresh))).StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshAgain = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        refreshAgain.StatusCode.Should().Be(HttpStatusCode.OK, "each refresh left the browser with the next token");

        var logout = await _browser.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterLogout = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        afterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Session_WhenARefreshTokenIsStolen_EndsForBothTheThiefAndTheVictim()
    {
        var user = await CreateUser();
        var login = await LoginInBrowser(user.Email, Password);
        var stolen = RefreshTokenFrom(login);

        (await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Refresh(stolen)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var victim = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        victim.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var db = Api.Postgres.CreateContext();
        (await db.RefreshTokens.Where(token => token.UserId == user.Id).ToListAsync()).Should().OnlyContain(token => token.IsRevoked);
    }

    [Fact]
    public async Task Login_AfterTenFailures_IsLockedFor15MinutesAndThenOpensAgain()
    {
        var user = await CreateUser();
        for (var attempt = 0; attempt < FailuresBeforeTheLock - 1; attempt++)
        {
            (await Login(user.Email, WrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await LockedUntil(user)).Should().BeNull("nine failures aren't enough");
        (await Login(user.Email, WrongPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var lockedUntil = await LockedUntil(user);
        lockedUntil.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        var whileLocked = await Login(user.Email, Password);
        whileLocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await whileLocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("Auth.InvalidCredentials");

        await MoveLockIntoThePast(user);

        (await Login(user.Email, Password)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LockedUntil(user)).Should().BeNull();
    }

    [Fact]
    public async Task Session_WhenThePasswordIsForcedToChange_GoesFromFirstLoginToWork()
    {
        var user = await CreateUser(mustChangePassword: true);

        var first = await LoginInBrowser(user.Email, Password);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        // The browser reloads the page: the access token is gone, and the cookie brings back a session.
        var reloaded = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        var accessToken = await AccessTokenFrom(reloaded);
        (await GetProtected(accessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = Password, newPassword = NewPassword }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var changed = await _browser.SendAsync(change);
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetProtected(await AccessTokenFrom(changed))).StatusCode.Should().Be(HttpStatusCode.OK);

        // The cookie that came with the change is the one that keeps the session going.
        var afterChange = await _browser.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);
        afterChange.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetMe(await AccessTokenFrom(afterChange))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> LoginInBrowser(string email, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-Forwarded-For", UnusedAddress());
        return await _browser.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetMe(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetProtected(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    private async Task<DateTimeOffset?> LockedUntil(User user)
    {
        await using var db = Api.Postgres.CreateContext();
        return (await db.Users.SingleAsync(existing => existing.Id == user.Id)).LockedUntil;
    }

    private async Task MoveLockIntoThePast(User user)
    {
        await using var db = Api.Postgres.CreateContext();
        await db.Users.Where(existing => existing.Id == user.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.LockedUntil, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }
}
