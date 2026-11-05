using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Users;

/// <summary>Someone deactivated and brought back, seen over HTTP with what they held before.</summary>
public sealed class ReactivationFlowTests(ApiFixture api) : ManagementApiTest(api)
{
    [Fact]
    public async Task Login_AfterReactivation_WorksWithTheSamePassword()
    {
        var user = await CreateUser(UnusedEmail());
        var token = await DeactivateThroughTheApi(user);

        var activated = await Send(HttpMethod.Post, $"/api/users/{user.Id}/activate", token);
        var login = await Login(user.Email);

        activated.StatusCode.Should().Be(HttpStatusCode.OK);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Coming back starts no session: what ended with the deactivation stays ended.
    [Fact]
    public async Task Refresh_WithASessionFromBeforeTheDeactivation_StaysRefused()
    {
        var user = await CreateUser(UnusedEmail());
        var session = RefreshTokenFrom(await Login(user.Email));
        var token = await DeactivateThroughTheApi(user);

        (await Send(HttpMethod.Post, $"/api/users/{user.Id}/activate", token)).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await Refresh(session);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Activate_WithTheDeactivatedUsersOwnStillValidToken_IsRefused()
    {
        var manager = await CreateUser(UnusedEmail(), permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(manager.Email));
        await DeactivateThroughTheApi(manager);

        var response = await Send(HttpMethod.Post, $"/api/users/{manager.Id}/activate", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeFrom(response)).Should().Be("Auth.SessionEnded");
        await using var db = Api.Postgres.CreateContext();
        (await db.Users.SingleAsync(user => user.Id == manager.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Activate_OfSomeoneAboveTheCaller_Returns403WithACode()
    {
        var admin = await CreateAdmin();
        await DeactivateThroughTheApi(admin);
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Post, $"/api/users/{admin.Id}/activate", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("User.PermissionsBeyondYours");
        await using var db = Api.Postgres.CreateContext();
        (await db.Users.SingleAsync(user => user.Id == admin.Id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Activate_OfAMissingUser_Returns404WithACode()
    {
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/activate", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("User.NotFound");
    }

    private async Task<string> DeactivateThroughTheApi(User user)
    {
        var token = await AdminToken();
        var response = await Send(HttpMethod.Post, $"/api/users/{user.Id}/deactivate", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return token;
    }
}
