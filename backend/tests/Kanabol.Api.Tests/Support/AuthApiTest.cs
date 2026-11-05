using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Kanabol.Api.Tests.Support;

/// <summary>Calls the auth endpoints over HTTPS, as the browser does, since the refresh cookie is Secure.</summary>
public abstract class AuthApiTest(ApiFixture api) : ApiTest(api)
{
    protected const string Password = "corect cal baterie capsa";
    protected const string RefreshCookieName = "refresh_token";

    private static int _lastAddress;

    protected HttpClient Client { get; } = api.Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
    });

    public override async Task DisposeAsync()
    {
        Client.Dispose();
        await base.DisposeAsync();
    }

    protected static SetCookieHeaderValue RefreshCookieFrom(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList())
            .Single(cookie => cookie.Name == RefreshCookieName);

    protected static string RefreshTokenFrom(HttpResponseMessage response) => RefreshCookieFrom(response).Value.ToString();

    protected static async Task<string> AccessTokenFrom(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;

    /// <summary>An address nobody else has used, so the limit on logins per IP doesn't get in the way.</summary>
    protected static string UnusedAddress()
    {
        var next = Interlocked.Increment(ref _lastAddress);
        return $"10.{(next >> 16) & 255}.{(next >> 8) & 255}.{next & 255}";
    }

    protected async Task<User> CreateUser(string email = "ana@example.com", bool mustChangePassword = false, params string[] permissions)
    {
        await using var db = Api.Postgres.CreateContext();

        var role = new Role($"Role of {email}", isSystem: false);
        var granted = await db.Permissions.Where(permission => permissions.Contains(permission.Name)).ToListAsync();
        granted.ForEach(permission => role.Grant(permission.Id));

        var hash = Api.Factory.Services.GetRequiredService<IPasswordHasher>().Hash(Password);
        var builder = new UserBuilder().WithEmail(email).InRole(role).WithPasswordHash(hash);
        var user = (mustChangePassword ? builder.MustChangePassword() : builder).Build();

        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    protected Task<HttpResponseMessage> Login(string email, string password = Password) =>
        Login(email, password, UnusedAddress());

    protected async Task<HttpResponseMessage> Login(string email, string password, string fromAddress)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-Forwarded-For", fromAddress);
        return await Client.SendAsync(request);
    }

    protected Task<HttpResponseMessage> Refresh(string? refreshToken) => PostWithCookie("/api/auth/refresh", refreshToken);

    protected Task<HttpResponseMessage> Logout(string? refreshToken) => PostWithCookie("/api/auth/logout", refreshToken);

    private async Task<HttpResponseMessage> PostWithCookie(string path, string? refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (refreshToken is not null)
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={refreshToken}");
        }

        return await Client.SendAsync(request);
    }
}
