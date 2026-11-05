using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;

namespace Kanabol.Api.Tests.Auth;

public sealed class ChangePasswordEndpointTests(ApiFixture api) : AuthApiTest(api)
{
    private const string NewPassword = "alta parola complet noua";

    [Fact]
    public async Task ChangePassword_WithoutAToken_Returns401ProblemDetails()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Password, newPassword = NewPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task ChangePassword_WhenTheCurrentPasswordIsRight_ReturnsANewSession()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        var response = await ChangePassword(await AccessTokenFrom(login), Password, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        RefreshTokenFrom(response).Should().NotBe(RefreshTokenFrom(login));
    }

    [Fact]
    public async Task ChangePassword_WhenSuccessful_EndsEarlierSessionsAndKeepsTheNewOne()
    {
        var user = await CreateUser();
        var otherDevice = await Login(user.Email);
        var login = await Login(user.Email);

        var response = await ChangePassword(await AccessTokenFrom(login), Password, NewPassword);

        (await Refresh(RefreshTokenFrom(otherDevice))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Refresh(RefreshTokenFrom(response))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WhenSuccessful_OnlyTheNewPasswordSignsIn()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        await ChangePassword(await AccessTokenFrom(login), Password, NewPassword);

        (await Login(user.Email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(user.Email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WhenTheCurrentPasswordIsWrong_Returns400OnThatField()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        var response = await ChangePassword(await AccessTokenFrom(login), "nu este parola buna", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("currentPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePassword_WhenTheNewPasswordIsCommon_Returns400OnThatField()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        var response = await ChangePassword(await AccessTokenFrom(login), Password, "password123");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").TryGetProperty("newPassword", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AnyOtherEndpoint_WhenThePasswordMustChange_Returns403WithTheCode()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);

        var response = await GetProtected(await AccessTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("Auth.PasswordChangeRequired");
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnyOtherEndpoint_AfterTheForcedChange_WorksWithTheNewToken()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);

        var change = await ChangePassword(await AccessTokenFrom(login), Password, NewPassword);

        change.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetProtected(await AccessTokenFrom(change))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnyOtherEndpoint_AfterTheForcedChange_StaysClosedToTheOldToken()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);
        var accessToken = await AccessTokenFrom(login);

        await ChangePassword(accessToken, Password, NewPassword);

        (await GetProtected(accessToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnyOtherEndpoint_WhenThePasswordIsFine_IsOpen()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);

        (await GetProtected(await AccessTokenFrom(login))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_WhenThePasswordMustChange_StillWorksAndKeepsTheFlag()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);

        var response = await Refresh(RefreshTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
        (await GetProtected(body.GetProperty("accessToken").GetString()!)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_WhenThePasswordMustChange_StillWorksWithTheTokenAttached()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenFrom(login));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UnknownRoute_WhenThePasswordMustChange_IsStillA404()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/nowhere");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenFrom(login));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_WhenThePasswordMustChange_StaysOpen()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenFrom(login));

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> ChangePassword(string accessToken, string currentPassword, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword, newPassword }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> GetProtected(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/test-protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }
}
