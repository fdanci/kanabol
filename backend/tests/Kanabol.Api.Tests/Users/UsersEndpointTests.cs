using System.Net;
using System.Net.Http.Json;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Users;

public sealed class UsersEndpointTests(ApiFixture api) : ManagementApiTest(api)
{
    private const string Users = "/api/users";
    private const string InitialPassword = "o parola initiala buna";
    private const string SomeUser = "/api/users/019a0000-0000-7000-8000-000000000001";

    [Fact]
    public async Task Post_WhenAllowed_Returns201AndTheUserCanSignInAndMustChangeThePassword()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var roleId = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Post, Users, token, new { email = "Ana.Pop@Example.com", displayName = "Ana Pop", roleId, initialPassword = InitialPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain(InitialPassword).And.NotContainEquivalentOf("passwordHash");
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/users/{id}");
        body.RootElement.GetProperty("email").GetString().Should().Be("ana.pop@example.com");
        body.RootElement.GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();

        var login = await Login("ana.pop@example.com", InitialPassword);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("mustChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_WhenAllowed_ReturnsAPageWithoutAnyPasswordData()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var hash = (await CreateUser(UnusedEmail())).PasswordHash;

        var response = await Send(HttpMethod.Get, $"{Users}?page=1&pageSize=25", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContainEquivalentOf("passwordHash").And.NotContain(hash);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(25);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(3);
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Get_WithoutPaging_UsesTheFirstPageOf50()
    {
        var token = await TokenWith(Permissions.UsersManage);

        using var body = await JsonFrom(await Send(HttpMethod.Get, Users, token));

        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(50);
    }

    [Fact]
    public async Task Put_WhenAllowed_Returns200WithTheUser()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var user = await CreateUser(UnusedEmail());

        var response = await Send(HttpMethod.Put, $"{Users}/{user.Id}", token, new { email = "noua@example.com", displayName = "Noua" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("email").GetString().Should().Be("noua@example.com");
    }

    [Fact]
    public async Task PutRole_WhenAllowed_Returns200AndEndsTheUsersSession()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var user = await CreateUser(UnusedEmail());
        var session = RefreshTokenFrom(await Login(user.Email));
        var roleId = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Put, $"{Users}/{user.Id}/role", token, new { roleId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("roleName").GetString().Should().Be("Analist");
        (await Refresh(session)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Deactivate_WhenAllowed_Returns200()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var user = await CreateUser(UnusedEmail());

        var response = await Send(HttpMethod.Post, $"{Users}/{user.Id}/deactivate", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AssignableRoles_WhenAllowed_ReturnsOnlyWhatTheCallerCanGive()
    {
        var token = await TokenWith(Permissions.UsersManage);
        await CreateRoleInDatabase("Vizitator");
        await CreateRoleInDatabase("Sef", Permissions.ReportsView);

        var response = await Send(HttpMethod.Get, $"{Users}/assignable-roles", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        var names = body.RootElement.EnumerateArray().Select(role => role.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Vizitator").And.NotContain("Sef").And.NotContain(Role.AdminName);
    }

    [Theory]
    [InlineData("GET", Users)]
    [InlineData("POST", Users)]
    [InlineData("GET", "/api/users/assignable-roles")]
    [InlineData("GET", SomeUser)]
    [InlineData("PUT", SomeUser)]
    [InlineData("PUT", SomeUser + "/role")]
    [InlineData("POST", SomeUser + "/deactivate")]
    public async Task Endpoint_WithoutAToken_Returns401(string method, string path) =>
        (await Send(new HttpMethod(method), path, accessToken: null, BodyFor(method, path))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("GET", Users)]
    [InlineData("POST", Users)]
    [InlineData("GET", "/api/users/assignable-roles")]
    [InlineData("GET", SomeUser)]
    [InlineData("PUT", SomeUser)]
    [InlineData("PUT", SomeUser + "/role")]
    [InlineData("POST", SomeUser + "/deactivate")]
    public async Task Endpoint_WithoutUsersManage_Returns403WithACode(string method, string path)
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(new HttpMethod(method), path, token, BodyFor(method, path));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task Endpoint_WhenThePasswordMustChange_Returns403WithItsCode()
    {
        var user = await CreateUser(UnusedEmail(), mustChangePassword: true, permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(user.Email));

        var response = await Send(HttpMethod.Get, Users, token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PasswordChangeRequired");
    }

    [Fact]
    public async Task PutRole_WhenTheCallerGivesThemselvesAdmin_Returns403WithACode()
    {
        var manager = await CreateUser(UnusedEmail(), permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(manager.Email));

        var response = await Send(HttpMethod.Put, $"{Users}/{manager.Id}/role", token, new { roleId = await AdminRoleId() });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("User.RoleBeyondYours");
    }

    [Fact]
    public async Task Post_WithATakenEmail_Returns409WithACode()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var roleId = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Post, Users, token, new { email = "admin@kanabol.local", displayName = "Ana", roleId, initialPassword = InitialPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("User.EmailTaken");
    }

    [Fact]
    public async Task Post_WithAnUnknownRole_Returns400OnTheField()
    {
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Post, Users, token, new { email = "ana@example.com", displayName = "Ana", roleId = Guid.CreateVersion7(), initialPassword = InitialPassword });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").TryGetProperty("roleId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Post_WithABadPassword_Returns400WithoutEchoingIt()
    {
        var token = await TokenWith(Permissions.UsersManage);
        var roleId = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Post, Users, token, new { email = "ana@example.com", displayName = "Ana", roleId, initialPassword = "scurta" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var text = await response.Content.ReadAsStringAsync();
        text.Should().Contain("initialPassword").And.NotContain("scurta");
    }

    [Fact]
    public async Task Get_WithAMissingUser_Returns404WithACode()
    {
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Get, SomeUser, token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("User.NotFound");
    }

    private static object? BodyFor(string method, string path)
    {
        if (method == "POST" && path == Users)
        {
            return new { email = "ana@example.com", displayName = "Ana", roleId = Guid.CreateVersion7(), initialPassword = InitialPassword };
        }

        if (method != "PUT")
        {
            return null;
        }

        return path.EndsWith("/role", StringComparison.Ordinal)
            ? new { roleId = Guid.CreateVersion7() }
            : new { email = "ana@example.com", displayName = "Ana" };
    }
}
