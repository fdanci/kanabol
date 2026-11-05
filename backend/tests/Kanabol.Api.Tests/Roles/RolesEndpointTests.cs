using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;

namespace Kanabol.Api.Tests.Roles;

public sealed class RolesEndpointTests(ApiFixture api) : ManagementApiTest(api)
{
    private const string Roles = "/api/roles";

    [Fact]
    public async Task Post_WhenAllowed_Returns201WithLocationAndTheRole()
    {
        var token = await TokenWith(Permissions.RolesManage, Permissions.ReportsView);

        var response = await Send(HttpMethod.Post, Roles, token, RoleBody("Analist", Permissions.ReportsView));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = await JsonFrom(response);
        var id = body.RootElement.GetProperty("id").GetGuid();
        response.Headers.Location!.ToString().Should().Be($"/api/roles/{id}");
        body.RootElement.GetProperty("name").GetString().Should().Be("Analist");
        body.RootElement.GetProperty("isSystem").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("userCount").GetInt32().Should().Be(0);
        body.RootElement.GetProperty("permissions")[0].GetString().Should().Be(Permissions.ReportsView);

        (await Send(HttpMethod.Get, response.Headers.Location.ToString(), token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_WhenAllowed_ReturnsAPage()
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Get, $"{Roles}?page=1&pageSize=25", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(25);
        body.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        body.RootElement.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Get_WithoutPaging_UsesTheFirstPageOf50()
    {
        var token = await TokenWith(Permissions.RolesManage);

        using var body = await JsonFrom(await Send(HttpMethod.Get, Roles, token));

        body.RootElement.GetProperty("page").GetInt32().Should().Be(1);
        body.RootElement.GetProperty("pageSize").GetInt32().Should().Be(50);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("page=abc")]
    public async Task Get_WithBadPaging_Returns400(string query)
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Get, $"{Roles}?{query}", token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Put_WhenAllowed_Returns200WithTheRole()
    {
        var token = await TokenWith(Permissions.RolesManage);
        var id = await CreateRoleInDatabase("Analist");

        var response = await Send(HttpMethod.Put, $"{Roles}/{id}", token, RoleBody("Programator"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("name").GetString().Should().Be("Programator");
    }

    [Fact]
    public async Task Delete_WhenUnused_Returns204AndTheRoleIsGone()
    {
        var token = await TokenWith(Permissions.RolesManage);
        var id = await CreateRoleInDatabase("Analist");

        (await Send(HttpMethod.Delete, $"{Roles}/{id}", token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await Send(HttpMethod.Get, $"{Roles}/{id}", token);
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(after)).Should().Be("Role.NotFound");
    }

    [Fact]
    public async Task Permissions_WhenAllowed_ReturnsEveryOne()
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Get, "/api/permissions", token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = await JsonFrom(response);
        body.RootElement.EnumerateArray().Select(name => name.GetString()).Should().BeEquivalentTo(Permissions.All);
    }

    [Theory]
    [InlineData("GET", "/api/roles")]
    [InlineData("POST", "/api/roles")]
    [InlineData("GET", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("PUT", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("GET", "/api/permissions")]
    public async Task Endpoint_WithoutAToken_Returns401(string method, string path) =>
        (await Send(new HttpMethod(method), path, accessToken: null, BodyFor(method))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Theory]
    [InlineData("GET", "/api/roles")]
    [InlineData("POST", "/api/roles")]
    [InlineData("GET", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("PUT", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("DELETE", "/api/roles/019a0000-0000-7000-8000-000000000001")]
    [InlineData("GET", "/api/permissions")]
    public async Task Endpoint_WithoutRolesManage_Returns403WithACode(string method, string path)
    {
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(new HttpMethod(method), path, token, BodyFor(method));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    // The role lost it a moment ago, and the token still lists it.
    [Fact]
    public async Task Post_WhenOnlyTheTokenHasRolesManage_Returns403()
    {
        var user = await CreateUser(UnusedEmail());
        var token = Api.Factory.CreateToken(user.Id, Permissions.RolesManage);

        var response = await Send(HttpMethod.Post, Roles, token, BodyFor("POST"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Fact]
    public async Task Post_ForAUserWhoNoLongerExists_Returns401()
    {
        var token = Api.Factory.CreateToken(Guid.CreateVersion7(), Permissions.RolesManage);

        var response = await Send(HttpMethod.Post, Roles, token, BodyFor("POST"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CodeFrom(response)).Should().Be("Auth.SessionEnded");
    }

    [Fact]
    public async Task Put_OnAdmin_Returns409WithACode()
    {
        var response = await Send(HttpMethod.Put, $"{Roles}/{await AdminRoleId()}", await AdminToken(), RoleBody("Sef", [.. Permissions.All]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Role.SystemRoleIsFixed");
    }

    [Fact]
    public async Task Delete_OnAdmin_Returns409WithACode()
    {
        var response = await Send(HttpMethod.Delete, $"{Roles}/{await AdminRoleId()}", await AdminToken());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Role.SystemRoleIsFixed");
    }

    [Fact]
    public async Task Post_WithAPermissionTheCallerLacks_Returns403WithACode()
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Post, Roles, token, RoleBody("Analist", Permissions.UsersManage));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Role.PermissionsBeyondYours");
    }

    [Fact]
    public async Task Post_WithATakenName_Returns409WithACode()
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Post, Roles, token, RoleBody("admin"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeFrom(response)).Should().Be("Role.NameTaken");
    }

    [Fact]
    public async Task Post_WithAnUnknownPermission_Returns400OnTheField()
    {
        var token = await TokenWith(Permissions.RolesManage);

        var response = await Send(HttpMethod.Post, Roles, token, RoleBody("Analist", "no.such.permission"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("errors").EnumerateObject().Should().ContainSingle(field => field.Name.StartsWith("permissions", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{ "permissions": [] }""")]
    [InlineData("""{ "name": "Analist" }""")]
    [InlineData("""{ "name": null, "permissions": [] }""")]
    [InlineData("""{ "name": "Analist", "permissions": [null] }""")]
    public async Task Post_WithAMissingOrNullField_Returns400(string json)
    {
        var token = await TokenWith(Permissions.RolesManage);
        using var request = new HttpRequestMessage(HttpMethod.Post, Roles)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_WithAnIdThatIsNotAGuid_Returns404()
    {
        var token = await TokenWith(Permissions.RolesManage);

        (await Send(HttpMethod.Get, $"{Roles}/not-a-guid", token)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static object RoleBody(string name, params string[] permissions) => new { name, permissions };

    private static object? BodyFor(string method) =>
        method is "POST" or "PUT" ? RoleBody("Analist") : null;
}
