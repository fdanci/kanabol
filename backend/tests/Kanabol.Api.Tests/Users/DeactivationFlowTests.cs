using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Users;

/// <summary>What's left of someone after they're deactivated, seen over HTTP with what they still hold.</summary>
public sealed class DeactivationFlowTests(ApiFixture api) : ManagementApiTest(api)
{
    private const string InitialPassword = "o parola initiala buna";

    [Fact]
    public async Task Login_AfterDeactivation_IsRefusedLikeAWrongPassword()
    {
        var user = await CreateUser(UnusedEmail());
        await DeactivateThroughTheApi(user);

        var response = await Login(user.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeFrom(response)).Should().Be("Auth.InvalidCredentials");
    }

    [Fact]
    public async Task Refresh_AfterDeactivation_IsRefusedAndClearsTheCookie()
    {
        var user = await CreateUser(UnusedEmail());
        var session = RefreshTokenFrom(await Login(user.Email));
        await DeactivateThroughTheApi(user);

        var response = await Refresh(session);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        RefreshCookieFrom(response).Value.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task Me_WithAStillValidToken_SaysTheSessionEnded()
    {
        var user = await CreateUser(UnusedEmail());
        var token = await AccessTokenFrom(await Login(user.Email));
        await DeactivateThroughTheApi(user);

        var response = await Send(HttpMethod.Get, "/api/auth/me", token);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeFrom(response)).Should().Be("Auth.SessionEnded");
    }

    // The window that matters: a dismissed admin must not be able to make themselves a new account.
    [Fact]
    public async Task ManagingUsers_WithAStillValidToken_IsRefusedAndCreatesNothing()
    {
        var manager = await CreateUser(UnusedEmail(), permissions: [Permissions.UsersManage, Permissions.RolesManage]);
        var token = await AccessTokenFrom(await Login(manager.Email));
        await DeactivateThroughTheApi(manager);

        var created = await Send(HttpMethod.Post, "/api/users", token, new
        {
            email = "cont.nou@example.com",
            displayName = "Cont nou",
            roleId = manager.RoleId,
            initialPassword = InitialPassword,
        });
        var role = await Send(HttpMethod.Post, "/api/roles", token, new { name = "Rol nou", permissions = Array.Empty<string>() });

        created.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeFrom(created)).Should().Be("Auth.SessionEnded");
        role.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var db = Api.Postgres.CreateContext();
        (await db.Users.AnyAsync(user => user.Email == "cont.nou@example.com")).Should().BeFalse();
        (await db.Roles.AnyAsync(existing => existing.Name == "Rol nou")).Should().BeFalse();
    }

    [Fact]
    public async Task ProjectEndpoint_WithAStillValidToken_IsRefused()
    {
        var user = await CreateUser(UnusedEmail());
        var project = new ProjectBuilder().OwnedBy(user).Build();
        await using (var db = Api.Postgres.CreateContext())
        {
            db.Projects.Add(project);
            await db.SaveChangesAsync();
        }

        var token = await AccessTokenFrom(await Login(user.Email));
        await DeactivateThroughTheApi(user);

        var response = await Send(HttpMethod.Get, $"/api/test-project-access/{project.Id}/owner", token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Project.OwnerRequired");
    }

    // Spelled out because it's a known limit, not an oversight: an endpoint that only reads the token
    // can't know, until the token expires. Managing users and projects don't work this way.
    [Fact]
    public async Task EndpointThatOnlyReadsTheToken_StillOpensUntilItExpires()
    {
        var user = await CreateUser(UnusedEmail(), permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(user.Email));
        await DeactivateThroughTheApi(user);

        (await Send(HttpMethod.Get, "/api/test-permissions/users", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task List_AfterDeactivation_StillShowsTheUser()
    {
        var user = await CreateUser(UnusedEmail());
        var token = await DeactivateThroughTheApi(user);

        using var body = await JsonFrom(await Send(HttpMethod.Get, $"/api/users/{user.Id}", token));

        body.RootElement.GetProperty("email").GetString().Should().Be(user.Email);
        body.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    // Their name stays on what they did, so the address can't go to someone new.
    [Fact]
    public async Task Create_WithTheEmailOfADeactivatedUser_Returns409()
    {
        var user = await CreateUser(UnusedEmail());
        var token = await DeactivateThroughTheApi(user);

        var response = await Send(HttpMethod.Post, "/api/users", token, new
        {
            email = user.Email,
            displayName = "Altcineva",
            roleId = user.RoleId,
            initialPassword = InitialPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("User.EmailTaken");
    }

    [Fact]
    public async Task Deactivate_OfTheLastAdmin_Returns409WithACode()
    {
        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Users.ExecuteUpdateAsync(set => set.SetProperty(user => user.IsActive, false));
        }

        var admin = await CreateAdmin();
        var token = await AccessTokenFrom(await Login(admin.Email));

        var response = await Send(HttpMethod.Post, $"/api/users/{admin.Id}/deactivate", token);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("User.LastAdmin");
    }

    [Fact]
    public async Task ChangeRole_OfTheLastAdmin_Returns409WithACode()
    {
        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Users.ExecuteUpdateAsync(set => set.SetProperty(user => user.IsActive, false));
        }

        var admin = await CreateAdmin();
        var token = await AccessTokenFrom(await Login(admin.Email));
        var roleId = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Put, $"/api/users/{admin.Id}/role", token, new { roleId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("User.LastAdmin");
    }

    private async Task<string> DeactivateThroughTheApi(User user)
    {
        var token = await AdminToken();
        var response = await Send(HttpMethod.Post, $"/api/users/{user.Id}/deactivate", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return token;
    }
}
