using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>What [HasPermission] does to an endpoint, over HTTP and through the real pipeline.</summary>
public sealed class PermissionAuthorizationTests(ApiFixture api) : ApiTest(api)
{
    private static readonly Uri _users = new("/api/test-permissions/users", UriKind.Relative);
    private static readonly Uri _usersAndRoles = new("/api/test-permissions/users-and-roles", UriKind.Relative);
    private static readonly Uri _nobody = new("/api/test-permissions/nobody", UriKind.Relative);
    private static readonly Uri _onController = new("/api/test-permissions-on-controller", UriKind.Relative);

    [Fact]
    public async Task Endpoint_WithoutAToken_Returns401()
    {
        using var client = Api.Factory.CreateClient();

        (await client.GetAsync(_users)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Endpoint_WithThePermission_Returns200() =>
        (await GetAs(_users, Permissions.UsersManage)).Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task Endpoint_WithThePermissionAmongOthers_Returns200() =>
        (await GetAs(_users, Permissions.ReportsView, Permissions.UsersManage, Permissions.TasksArchive)).Should().Be(HttpStatusCode.OK);

    [Fact]
    public async Task Endpoint_WithoutAnyPermission_Returns403() =>
        (await GetAs(_users)).Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Endpoint_WithOnlyOtherPermissions_Returns403() =>
        (await GetAs(_users, Permissions.RolesManage, Permissions.ReportsView)).Should().Be(HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("USERS.MANAGE")]
    [InlineData("users.manage ")]
    [InlineData("users")]
    public async Task Endpoint_WithASimilarName_Returns403(string similar) =>
        (await GetAs(_users, similar)).Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Endpoint_WithTwoAttributes_NeedsBoth()
    {
        (await GetAs(_usersAndRoles, Permissions.UsersManage)).Should().Be(HttpStatusCode.Forbidden);
        (await GetAs(_usersAndRoles, Permissions.RolesManage)).Should().Be(HttpStatusCode.Forbidden);
        (await GetAs(_usersAndRoles, Permissions.UsersManage, Permissions.RolesManage)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_WithAPermissionMisspeltInCode_StaysClosedEvenToAnAdmin() =>
        (await GetAs(_nobody, [.. Permissions.All])).Should().Be(HttpStatusCode.Forbidden);

    [Fact]
    public async Task Controller_WithAPermission_NeedsItOnEveryAction()
    {
        (await GetAs(_onController)).Should().Be(HttpStatusCode.Forbidden);
        (await GetAs(_onController, Permissions.ReportsView)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Action_WithAPermissionOfItsOwn_AlsoNeedsTheControllers()
    {
        (await PostAs(_onController, Permissions.ProjectsCreate)).Should().Be(HttpStatusCode.Forbidden);
        (await PostAs(_onController, Permissions.ReportsView)).Should().Be(HttpStatusCode.Forbidden);
        (await PostAs(_onController, Permissions.ReportsView, Permissions.ProjectsCreate)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Endpoint_WithoutHasPermission_StillOnlyNeedsASignedInUser()
    {
        var protectedPath = new Uri("/api/test-protected", UriKind.Relative);
        using var anonymous = Api.Factory.CreateClient();

        (await anonymous.GetAsync(protectedPath)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await GetAs(protectedPath)).Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> GetAs(Uri path, params string[] permissions)
    {
        using var client = Api.Factory.CreateClient(Guid.CreateVersion7(), permissions);
        return (await client.GetAsync(path)).StatusCode;
    }

    private async Task<HttpStatusCode> PostAs(Uri path, params string[] permissions)
    {
        using var client = Api.Factory.CreateClient(Guid.CreateVersion7(), permissions);
        return (await client.PostAsync(path, content: null)).StatusCode;
    }
}
