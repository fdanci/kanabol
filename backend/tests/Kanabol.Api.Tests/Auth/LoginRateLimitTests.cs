using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Auth;

public sealed class LoginRateLimitTests(ApiFixture api) : AuthApiTest(api)
{
    private const int AllowedPerMinute = 5;
    private const string WrongPassword = "nu este parola buna";

    [Fact]
    public async Task Login_UpToTheLimit_AnswersNormally()
    {
        var user = await CreateUser();
        var address = UnusedAddress();

        for (var attempt = 0; attempt < AllowedPerMinute; attempt++)
        {
            (await Login(user.Email, WrongPassword, address)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Login_OverTheLimit_Returns429ProblemDetailsWithRetryAfter()
    {
        var user = await CreateUser();
        var address = UnusedAddress();
        await UseUpTheLimit(user.Email, address);

        var response = await Login(user.Email, WrongPassword, address);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(429);
        body.GetProperty("code").GetString().Should().Be("Auth.TooManyLoginAttempts");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        response.Headers.RetryAfter!.Delta.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Login_OverTheLimit_RefusesEvenTheRightPassword()
    {
        var user = await CreateUser();
        var address = UnusedAddress();
        await UseUpTheLimit(user.Email, address);

        var response = await Login(user.Email, Password, address);

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_WhenAttemptsSucceed_StillCountsThem()
    {
        var user = await CreateUser();
        var address = UnusedAddress();
        for (var attempt = 0; attempt < AllowedPerMinute; attempt++)
        {
            (await Login(user.Email, Password, address)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await Login(user.Email, Password, address)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Login_FromAnotherAddress_IsNotAffected()
    {
        var user = await CreateUser();
        await UseUpTheLimit(user.Email, UnusedAddress());

        (await Login(user.Email, Password, UnusedAddress())).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_OverTheLimit_DoesNotCountAgainstTheAccount()
    {
        var user = await CreateUser();
        var address = UnusedAddress();
        await UseUpTheLimit(user.Email, address);

        await Login(user.Email, WrongPassword, address);

        await using var db = Api.Postgres.CreateContext();
        (await db.Users.SingleAsync(existing => existing.Id == user.Id)).FailedLoginAttempts.Should().Be(AllowedPerMinute);
    }

    [Fact]
    public async Task OtherAuthEndpoints_OverTheLoginLimit_StillAnswer()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);
        for (var attempt = 0; attempt < AllowedPerMinute + 1; attempt++)
        {
            (await Logout(null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await Refresh(RefreshTokenFrom(login))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task UseUpTheLimit(string email, string address)
    {
        for (var attempt = 0; attempt < AllowedPerMinute; attempt++)
        {
            await Login(email, WrongPassword, address);
        }
    }
}
