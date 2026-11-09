using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Support;

/// <summary>Calls the endpoints that manage users and roles, as a user who really signed in.</summary>
public abstract class ManagementApiTest(ApiFixture api) : AuthApiTest(api)
{
    private static int _lastEmail;

    protected static string UnusedEmail() => $"user{Interlocked.Increment(ref _lastEmail)}@example.com";

    protected static async Task<string?> CodeFrom(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    protected static async Task<JsonDocument> JsonFrom(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    protected async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? accessToken, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    protected async Task<string> TokenWith(params string[] permissions)
    {
        var user = await CreateUser(UnusedEmail(), permissions: permissions);
        return await AccessTokenFrom(await Login(user.Email));
    }

    /// <summary>A user in the seeded Admin role, whose password is known and doesn't have to change.</summary>
    protected async Task<User> CreateAdmin()
    {
        await using var db = Api.Postgres.CreateContext();

        var role = await db.Roles.SingleAsync(existing => existing.IsSystem);
        var user = new UserBuilder().WithEmail(UnusedEmail()).InRole(role).WithPasswordHash(PasswordHash).Build();

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected async Task<string> AdminToken() => await AccessTokenFrom(await Login((await CreateAdmin()).Email));

    protected async Task<Guid> CreateRoleInDatabase(string name, params string[] permissions)
    {
        await using var db = Api.Postgres.CreateContext();

        var role = new Role(name, isSystem: false);
        var granted = await db.Permissions.Where(permission => permissions.Contains(permission.Name)).ToListAsync();
        granted.ForEach(permission => role.Grant(permission.Id));

        db.Roles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    protected async Task<Guid> AdminRoleId()
    {
        await using var db = Api.Postgres.CreateContext();
        return await db.Roles.Where(role => role.IsSystem).Select(role => role.Id).SingleAsync();
    }
}
