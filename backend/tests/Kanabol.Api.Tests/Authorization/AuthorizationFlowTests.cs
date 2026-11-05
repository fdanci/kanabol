using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Tests.Shared.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>Whole paths over HTTP: a role in the database, a token from a real login, and what it opens.</summary>
public sealed class AuthorizationFlowTests(ApiFixture api) : AuthApiTest(api)
{
    private const string PermissionPath = "/api/test-permissions/users";
    private const string CorrelationId = "from-the-authorization-test";

    [Fact]
    public async Task Endpoint_WithoutThePermission_Returns403ProblemDetailsWithACode()
    {
        var user = await CreateUser(permissions: [Permissions.ReportsView]);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, PermissionPath);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "Auth.PermissionRequired");
    }

    [Fact]
    public async Task Endpoint_WithoutThePermission_DoesNotSayWhichOneIsMissing()
    {
        var user = await CreateUser();
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, PermissionPath);

        (await response.Content.ReadAsStringAsync()).Should().NotContain(Permissions.UsersManage);
    }

    [Fact]
    public async Task Endpoint_WithoutAToken_Returns401ProblemDetails()
    {
        var response = await Client.GetAsync(new Uri(PermissionPath, UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Login_WithARoleThatHasThePermission_OpensTheEndpoint()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(user.Email));

        (await GetWith(token, PermissionPath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_WhenThePasswordMustChange_RefusesEvenWithThePermission()
    {
        var user = await CreateUser(mustChangePassword: true, permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, PermissionPath);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "Auth.PasswordChangeRequired");
    }

    // The gate comes before the permission check: the answer has to say what the user can do about it.
    [Fact]
    public async Task Endpoint_WhenThePasswordMustChangeAndThePermissionIsMissing_SaysToChangeThePassword()
    {
        var user = await CreateUser(mustChangePassword: true);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, PermissionPath);

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "Auth.PasswordChangeRequired");
    }

    [Fact]
    public async Task SeededAdmin_AfterChangingThePassword_HasEveryPermission()
    {
        var firstToken = await AccessTokenFrom(await Login("admin@kanabol.local", "only-for-these-tests"));
        (await GetWith(firstToken, PermissionPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var change = new HttpRequestMessage(HttpMethod.Post, "/api/auth/change-password")
        {
            Content = JsonContent.Create(new { currentPassword = "only-for-these-tests", newPassword = Password }),
        };
        change.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstToken);
        var changed = await Client.SendAsync(change);

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = await AccessTokenFrom(changed);
        (await GetWith(newToken, PermissionPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        new JsonWebToken(newToken).Claims.Where(claim => claim.Type == "permission").Select(claim => claim.Value)
            .Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Refresh_AfterTheRoleGotThePermission_OpensTheEndpoint()
    {
        var user = await CreateUser();
        var login = await Login(user.Email);
        var firstToken = await AccessTokenFrom(login);
        (await GetWith(firstToken, PermissionPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await using (var db = Api.Postgres.CreateContext())
        {
            var permission = await db.Permissions.SingleAsync(existing => existing.Name == Permissions.UsersManage);
            var role = await db.Roles.Include(existing => existing.RolePermissions).SingleAsync(existing => existing.Id == user.RoleId);
            role.Grant(permission.Id);
            await db.SaveChangesAsync();
        }

        // Until then, the token it already has keeps saying what it said.
        (await GetWith(firstToken, PermissionPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var refreshed = await Refresh(RefreshTokenFrom(login));
        (await GetWith(await AccessTokenFrom(refreshed), PermissionPath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refresh_AfterTheRoleLostThePermission_ClosesTheEndpoint()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage]);
        var login = await Login(user.Email);
        (await GetWith(await AccessTokenFrom(login), PermissionPath)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var db = Api.Postgres.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"delete from role_permissions where role_id = {user.RoleId}");
        }

        var refreshed = await Refresh(RefreshTokenFrom(login));
        (await GetWith(await AccessTokenFrom(refreshed), PermissionPath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ProjectEndpoint_ForANonMember_Returns403ProblemDetailsWithACode()
    {
        var user = await CreateUser();
        var project = await CreateProject();
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, $"/api/test-project-access/{project.Id}/member");

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "Project.MemberRequired");
    }

    [Fact]
    public async Task ProjectEndpoint_ForAMember_Opens()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));
        var token = await AccessTokenFrom(await Login(user.Email));

        (await GetWith(token, $"/api/test-project-access/{project.Id}/member")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ProjectEndpoint_WithViewAll_OpensWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject();
        var token = await AccessTokenFrom(await Login(user.Email));

        (await GetWith(token, $"/api/test-project-access/{project.Id}/member")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // On the seeded database, where the permission that would open the project has its row.
    [Theory]
    [InlineData("member", "Project.MemberRequired")]
    [InlineData("owner", "Project.OwnerRequired")]
    public async Task ProjectEndpoint_WithAnUnrelatedPermission_Returns403(string asked, string code)
    {
        var user = await CreateUser(permissions: [Permissions.ReportsView]);
        var project = await CreateProject();
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, $"/api/test-project-access/{project.Id}/{asked}");

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, code);
    }

    [Fact]
    public async Task ProjectEndpoint_ForAMemberAskingAsOwner_Returns403ProblemDetailsWithACode()
    {
        var user = await CreateUser();
        var project = await CreateProject(builder => builder.WithMember(user));
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await GetWith(token, $"/api/test-project-access/{project.Id}/owner");

        await ShouldBeProblem(response, HttpStatusCode.Forbidden, "Project.OwnerRequired");
    }

    [Fact]
    public async Task ProjectEndpoint_WithManage_OpensAsOwnerWithoutBeingAMember()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsManage]);
        var project = await CreateProject();
        var token = await AccessTokenFrom(await Login(user.Email));

        (await GetWith(token, $"/api/test-project-access/{project.Id}/owner")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // The role lost the permission a moment ago, and the token still lists it: the database decides.
    [Fact]
    public async Task ProjectEndpoint_WithATokenThatStillListsViewAll_Returns403()
    {
        var user = await CreateUser();
        var project = await CreateProject();
        var token = Api.Factory.CreateToken(user.Id, Permissions.ProjectsViewAll);

        var response = await GetWith(token, $"/api/test-project-access/{project.Id}/member");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static async Task ShouldBeProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.Should().Be(status);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().Should().Be((int)status);
        body.RootElement.GetProperty("code").GetString().Should().Be(code);
        body.RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        body.RootElement.GetProperty("correlationId").GetString().Should().Be(CorrelationId);
    }

    private async Task<HttpResponseMessage> GetWith(string accessToken, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("X-Correlation-Id", CorrelationId);
        return await Client.SendAsync(request);
    }

    private async Task<Project> CreateProject(Func<ProjectBuilder, ProjectBuilder>? configure = null)
    {
        var project = (configure ?? (builder => builder))(new ProjectBuilder()).Build();

        await using var db = Api.Postgres.CreateContext();
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project;
    }
}
