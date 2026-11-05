using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Auth;

public sealed class CurrentUserEndpointTests(ApiFixture api) : AuthApiTest(api)
{
    [Fact]
    public async Task Me_WithoutAToken_Returns401ProblemDetails()
    {
        var response = await Client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Me_WithATokenFromLogin_ReturnsTheUserAndPermissions()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage, Permissions.ReportsView]);
        var login = await Login(user.Email);

        var response = await GetMe(await AccessTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetGuid().Should().Be(user.Id);
        body.GetProperty("email").GetString().Should().Be(user.Email);
        body.GetProperty("displayName").GetString().Should().Be(user.DisplayName);
        body.GetProperty("roleName").GetString().Should().Be($"Role of {user.Email}");
        body.GetProperty("mustChangePassword").GetBoolean().Should().BeFalse();
        body.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString())
            .Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
    }

    [Fact]
    public async Task Me_WhenThePasswordMustChange_StillAnswers()
    {
        var user = await CreateUser(mustChangePassword: true);
        var login = await Login(user.Email);

        var response = await GetMe(await AccessTokenFrom(login));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_AsTheSeededAdmin_HasEveryPermission()
    {
        var login = await Login("admin@kanabol.local", "only-for-these-tests");

        var response = await GetMe(await AccessTokenFrom(login));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("permissions").EnumerateArray().Select(permission => permission.GetString())
            .Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Me_WithATokenForAUserWhoIsGone_Returns401()
    {
        var response = await GetMe(Api.Factory.CreateToken(Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("Auth.SessionEnded");
    }

    [Fact]
    public void CurrentUser_FromARequestWithClaims_ReadsThem()
    {
        var userId = Guid.CreateVersion7();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(AuthClaims.Subject, userId.ToString()),
                new Claim(AuthClaims.Permission, Permissions.UsersManage),
            ],
            authenticationType: "test"));

        using var scope = Api.Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = principal };
        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        currentUser.IsAuthenticated.Should().BeTrue();
        currentUser.Id.Should().Be(userId);
        currentUser.HasPermission(Permissions.UsersManage).Should().BeTrue();
        currentUser.HasPermission(Permissions.ReportsView).Should().BeFalse();
    }

    private async Task<HttpResponseMessage> GetMe(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await Client.SendAsync(request);
    }
}
