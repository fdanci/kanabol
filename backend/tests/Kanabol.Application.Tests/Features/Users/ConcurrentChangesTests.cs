using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.ActivateUser;
using Kanabol.Application.Features.Users.Commands.ChangeUserRole;
using Kanabol.Application.Features.Users.Commands.DeactivateUser;
using Kanabol.Application.Features.Users.Commands.ResetUserPassword;
using Kanabol.Application.Features.Users.Commands.UpdateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Users;

/// <summary>
/// A request that read a user, and another one that changed that user before the first one saved. The
/// first one must not write over what it didn't see: it gets a conflict, and changes nothing.
/// </summary>
public sealed class ConcurrentChangesTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    // The order the review found: B is read in an ordinary role, then made an admin, then the last
    // other admin leaves counting on B, and only then does the first request take B away.
    [Fact]
    public async Task Deactivate_OfAUserWhoBecameTheLastAdminMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var user = await CreateUser("b@example.com");
        SignIn(admin);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<DeactivateUserCommand, UserDto>(new DeactivateUserCommand(user.Id)),
            async () =>
            {
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId))).IsSuccess.Should().BeTrue();
                (await HandleInOwnScope<DeactivateUserCommand, UserDto>(new DeactivateUserCommand(admin.Id))).IsSuccess.Should().BeTrue();
            });

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await ShouldHaveOneActiveAdmin(admin.RoleId);
    }

    [Fact]
    public async Task ChangeRole_OfAUserWhoBecameTheLastAdminMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var user = await CreateUser("b@example.com");
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, role.Id)),
            async () =>
            {
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId))).IsSuccess.Should().BeTrue();
                (await HandleInOwnScope<DeactivateUserCommand, UserDto>(new DeactivateUserCommand(admin.Id))).IsSuccess.Should().BeTrue();
            });

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        await ShouldHaveOneActiveAdmin(admin.RoleId);
        (await Reload(user)).RoleId.Should().Be(admin.RoleId);
    }

    // B is read deactivated, so taking B out of Admin counts for nothing. Then B comes back, and the
    // last other admin leaves counting on B: the first request mustn't take B away after that.
    [Fact]
    public async Task ChangeRole_OfADeactivatedAdminWhoIsReactivatedMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var other = await CreateAdmin("b@example.com");
        await Deactivate(other);
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(other.Id, role.Id)),
            async () =>
            {
                (await HandleInOwnScope<ActivateUserCommand, UserDto>(new ActivateUserCommand(other.Id))).IsSuccess.Should().BeTrue();
                (await HandleInOwnScope<DeactivateUserCommand, UserDto>(new DeactivateUserCommand(admin.Id))).IsSuccess.Should().BeTrue();
            });

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        await ShouldHaveOneActiveAdmin(admin.RoleId);
        (await Reload(other)).RoleId.Should().Be(admin.RoleId);
    }

    // A manager may bring B back while B is in a role they cover, but not once an admin put B above them.
    [Fact]
    public async Task Activate_ByAManager_OfAUserPutAboveThemMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        await Deactivate(user);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ActivateUserCommand, UserDto>(new ActivateUserCommand(user.Id), manager),
            async () =>
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId), admin)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).IsActive.Should().BeFalse();
    }

    // A manager may move B while B is in a role they cover, but not once an admin put B above them.
    [Fact]
    public async Task ChangeRole_ByAManager_DoesNotUndoAPromotionTheyCouldNotMake()
    {
        var admin = await CreateAdmin();
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, role.Id), manager),
            async () =>
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId), admin)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).RoleId.Should().Be(admin.RoleId);
    }

    [Fact]
    public async Task Update_OfAUserWhoWasPutAboveTheCallerMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateUserCommand, UserDto>(new UpdateUserCommand(user.Id, "altul@example.com", "Altul"), manager),
            async () =>
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId), admin)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).Email.Should().Be("b@example.com");
    }

    // Otherwise a manager could give a new password to someone an admin had just put above them.
    [Fact]
    public async Task ResetPassword_ByAManager_OfAUserPutAboveThemMeanwhile_IsRefused()
    {
        var admin = await CreateAdmin();
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<ResetUserPasswordCommand, UserDto>(new ResetUserPasswordCommand(user.Id, "o parola noua de la manager"), manager),
            async () =>
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, admin.RoleId), admin)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        var saved = await Reload(user);
        saved.PasswordHash.Should().Be(user.PasswordHash);
        saved.MustChangePassword.Should().BeFalse();
    }

    // One save: a session that changed after it was read makes the whole change fail, not half of it.
    [Fact]
    public async Task ChangeRole_WhenASessionChangesDuringTheSave_ChangesNothing()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        var token = await AddLiveToken(user);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            2,
            () => HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, role.Id), manager),
            () => RevokeElsewhere(token));

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).RoleId.Should().Be(user.RoleId);
    }

    [Fact]
    public async Task Deactivate_WhenASessionChangesDuringTheSave_ChangesNothing()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        var token = await AddLiveToken(user);

        var result = await PausedAtClockCall(
            2,
            () => HandleInOwnScope<DeactivateUserCommand, UserDto>(new DeactivateUserCommand(user.Id), manager),
            () => RevokeElsewhere(token));

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task ResetPassword_WhenASessionChangesDuringTheSave_ChangesNothing()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        var token = await AddLiveToken(user);

        var result = await PausedAtClockCall(
            2,
            () => HandleInOwnScope<ResetUserPasswordCommand, UserDto>(new ResetUserPasswordCommand(user.Id, "o parola noua de la manager"), manager),
            () => RevokeElsewhere(token));

        result.Error.Should().Be(UserErrors.ChangedMeanwhile);
        (await Reload(user)).PasswordHash.Should().Be(user.PasswordHash);
    }

    // The login read the user in their old role, so its token would carry the old permissions.
    [Fact]
    public async Task Login_WhenTheRoleChangesBeforeItSaves_StartsNoSession()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser("b@example.com");
        var role = await CreateRole("Analist");
        await using (var db = Postgres.CreateContext())
        {
            await db.Users.Where(existing => existing.Id == user.Id).ExecuteUpdateAsync(set => set.SetProperty(existing => existing.FailedLoginAttempts, 1));
        }

        var result = await PausedAtClockCall(
            2,
            () => HandleInOwnScope<LoginUserCommand, AuthSession>(new LoginUserCommand(user.Email, Password)),
            async () =>
                (await HandleInOwnScope<ChangeUserRoleCommand, UserDto>(new ChangeUserRoleCommand(user.Id, role.Id), manager)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(AuthErrors.SessionChangedMeanwhile);
        (await RefreshTokensOf(user)).Should().BeEmpty();
    }

    private async Task<RefreshToken> AddLiveToken(User user)
    {
        await using var db = Postgres.CreateContext();
        var token = new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    private async Task RevokeElsewhere(RefreshToken token)
    {
        await using var db = Postgres.CreateContext();
        await db.RefreshTokens.Where(existing => existing.Id == token.Id).ExecuteUpdateAsync(set => set.SetProperty(existing => existing.RevokedAt, Now));
    }

    private async Task ShouldHaveOneActiveAdmin(Guid adminRoleId)
    {
        await using var db = Postgres.CreateContext();
        (await db.Users.CountAsync(existing => existing.RoleId == adminRoleId && existing.IsActive)).Should().Be(1);
    }
}
