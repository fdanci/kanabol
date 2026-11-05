using System.Globalization;
using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Users;

/// <summary>A password an admin gave someone, seen over HTTP from both sides.</summary>
public sealed class PasswordResetFlowTests(ApiFixture api) : ManagementApiTest(api)
{
    private const string ResetPassword = "o parola noua de la admin";
    private const string ChosenPassword = "parola aleasa dupa resetare";

    [Fact]
    public async Task ChangePassword_AfterTheReset_TakesTheResetOneAsCurrent()
    {
        var user = await CreateUser(UnusedEmail());
        await ResetThroughTheApi(user);
        var forced = await AccessTokenFrom(await Login(user.Email, ResetPassword));

        var held = await Send(HttpMethod.Get, "/api/users", forced);
        var changed = await Send(HttpMethod.Post, "/api/auth/change-password", forced, new { currentPassword = ResetPassword, newPassword = ChosenPassword });

        (await CodeFrom(held)).Should().Be("Auth.PasswordChangeRequired");
        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Login(user.Email, ChosenPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_OfALockedOutUserAfterTheReset_Works()
    {
        var user = await CreateUser(UnusedEmail());
        for (var attempt = 0; attempt < User.MaxFailedLogins; attempt++)
        {
            (await Login(user.Email, "nu e parola lui")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await Login(user.Email)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ResetThroughTheApi(user);

        (await Login(user.Email, ResetPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // The window that matters: a manager whose account was taken back mustn't use the token still in
    // their hands to make themselves a new one.
    [Fact]
    public async Task ManagingUsers_WithATokenFromBeforeTheReset_IsRefusedAndCreatesNothing()
    {
        var manager = await CreateUser(UnusedEmail(), permissions: [Permissions.UsersManage]);
        var token = await AccessTokenFrom(await Login(manager.Email));
        await ResetThroughTheApi(manager);

        var response = await Send(HttpMethod.Post, "/api/users", token, new
        {
            email = "cont.nou@example.com",
            displayName = "Cont nou",
            roleId = manager.RoleId,
            initialPassword = ResetPassword,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PasswordChangeRequired");
        await using var db = Api.Postgres.CreateContext();
        (await db.Users.AnyAsync(user => user.Email == "cont.nou@example.com")).Should().BeFalse();
    }

    [Fact]
    public async Task ResetPassword_OfYourself_Returns403WithACode()
    {
        var admin = await CreateAdmin();
        var token = await AccessTokenFrom(await Login(admin.Email));

        var response = await Send(HttpMethod.Post, $"/api/users/{admin.Id}/reset-password", token, new { initialPassword = ResetPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("User.OwnPasswordReset");
        (await Login(admin.Email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_OfSomeoneAboveTheCaller_Returns403AndTheirPasswordStays()
    {
        var admin = await CreateAdmin();
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Post, $"/api/users/{admin.Id}/reset-password", token, new { initialPassword = ResetPassword });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("User.PermissionsBeyondYours");
        (await Login(admin.Email, ResetPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(admin.Email)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ResetPassword_OfAMissingUser_Returns404WithACode()
    {
        var token = await TokenWith(Permissions.UsersManage);

        var response = await Send(HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/reset-password", token, new { initialPassword = ResetPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(response)).Should().Be("User.NotFound");
    }

    [Fact]
    public async Task ResetPassword_WhenDone_IsLoggedWithoutThePassword()
    {
        var user = await CreateUser(UnusedEmail());

        await ResetThroughTheApi(user);

        var events = Api.Factory.Logs.Events.ToList();
        events.Select(logEvent => logEvent.RenderMessage(CultureInfo.InvariantCulture)).Should()
            .Contain(message => message.Contains("password reset", StringComparison.Ordinal) && message.Contains(user.Id.ToString(), StringComparison.Ordinal));
        events.Should().NotContain(logEvent =>
            logEvent.RenderMessage(CultureInfo.InvariantCulture).Contains(ResetPassword, StringComparison.Ordinal)
            || logEvent.Properties.Values.Any(value => value.ToString().Contains(ResetPassword, StringComparison.Ordinal)));
    }

    private async Task ResetThroughTheApi(User user)
    {
        var response = await Send(HttpMethod.Post, $"/api/users/{user.Id}/reset-password", await AdminToken(), new { initialPassword = ResetPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
