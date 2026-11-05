using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.CreateUser;
using Kanabol.Application.Features.Users.Commands.ResetUserPassword;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class ResetUserPasswordCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private const string ResetPassword = "o parola noua de la admin";

    private ICommandHandler<ResetUserPasswordCommand, UserDto> Handler => Get<ICommandHandler<ResetUserPasswordCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_SetsTheNewPasswordAndMakesThemChangeIt()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        result.Value.MustChangePassword.Should().BeTrue();
        var saved = await Reload(user);
        saved.MustChangePassword.Should().BeTrue();
        saved.PasswordHash.Should().NotContain(ResetPassword);
        Get<IPasswordHasher>().Verify(saved.PasswordHash, ResetPassword).Should().BeTrue();
        Get<IPasswordHasher>().Verify(saved.PasswordHash, Password).Should().BeFalse();
        saved.RoleId.Should().Be(user.RoleId);
        saved.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAllowed_EndsOnlyThatUsersSessions()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        await AddLiveToken(user);
        await AddLiveToken(other);

        await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        (await RefreshTokensOf(user)).Should().OnlyContain(token => token.IsRevoked && token.RevokedAt == Now);
        (await RefreshTokensOf(other)).Should().OnlyContain(token => !token.IsRevoked);
    }

    // The failures counted against the old password, and whoever forgot it is the one asking for this.
    [Fact]
    public async Task Handle_WhenTheUserWasLockedOut_UnlocksThem()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await LockOut(user);

        await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        var saved = await Reload(user);
        saved.FailedLoginAttempts.Should().Be(0);
        saved.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Handle_WhenAllowed_LogsItWithoutThePassword()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        Logs.Entries.Should().Contain(entry => entry.Message.Contains("password reset", StringComparison.Ordinal) && entry.Message.Contains(user.Id.ToString(), StringComparison.Ordinal));
        Logs.Entries.Should().NotContain(entry => entry.Message.Contains(ResetPassword, StringComparison.Ordinal));
    }

    // An admin may give a new password before bringing someone back; the reset alone doesn't.
    [Fact]
    public async Task Handle_OfADeactivatedUser_SetsThePasswordAndKeepsThemDeactivated()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(user);

        var result = await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
        var saved = await Reload(user);
        saved.IsActive.Should().BeFalse();
        Get<IPasswordHasher>().Verify(saved.PasswordHash, ResetPassword).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(new ResetUserPasswordCommand(Guid.CreateVersion7(), ResetPassword), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        await ShouldKeepTheirPassword(user);
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(manager);

        var result = await Handler.Handle(new ResetUserPasswordCommand(user.Id, ResetPassword), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        await ShouldKeepTheirPassword(user);
    }

    // Otherwise users.manage alone would be a way into an admin's account.
    [Fact]
    public async Task Handle_WhenTheUserHasPermissionsTheCallerLacks_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        var admin = await CreateAdmin();
        await AddLiveToken(admin);

        var result = await Handler.Handle(new ResetUserPasswordCommand(admin.Id, ResetPassword), CancellationToken.None);

        result.Error.Should().Be(UserErrors.PermissionsBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await ShouldKeepTheirPassword(admin);
        (await RefreshTokensOf(admin)).Should().OnlyContain(token => !token.IsRevoked);
    }

    // Your own goes through change-password, which asks for the current one: a stolen access token
    // mustn't be a way to take over the account it belongs to.
    [Fact]
    public async Task Handle_OfTheCallersOwnPassword_ReturnsForbiddenAndChangesNothing()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Handler.Handle(new ResetUserPasswordCommand(admin.Id, ResetPassword), CancellationToken.None);

        result.Error.Should().Be(UserErrors.OwnPasswordReset);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await ShouldKeepTheirPassword(admin);
    }

    [Theory]
    [InlineData("scurta")]
    [InlineData("password123")]
    public async Task Handle_WithAPasswordThePolicyRefuses_ReturnsTheFieldsErrorAndChangesNothing(string password)
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new ResetUserPasswordCommand(user.Id, password), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("InitialPassword");
        await ShouldKeepTheirPassword(user);
    }

    // The token a manager still holds after their password was reset carries no sign of it, so the
    // endpoints that manage users read it from the database, as they read a deactivation.
    [Fact]
    public async Task Handle_ThenTheResetUserManagesWithTheirOldToken_ReturnsPasswordChangeRequired()
    {
        var admin = await CreateAdmin();
        var manager = await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");
        (await HandleInOwnScope<ResetUserPasswordCommand, UserDto>(new ResetUserPasswordCommand(manager.Id, ResetPassword), admin)).IsSuccess.Should().BeTrue();

        var result = await Get<ICommandHandler<CreateUserCommand, UserDto>>()
            .Handle(new CreateUserCommand("cont.nou@example.com", "Cont nou", role.Id, ResetPassword), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PasswordChangeRequired);
        await using var db = Postgres.CreateContext();
        (await db.Users.AnyAsync(user => user.Email == "cont.nou@example.com")).Should().BeFalse();
    }

    private async Task AddLiveToken(User user)
    {
        await using var db = Postgres.CreateContext();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now));
        await db.SaveChangesAsync();
    }

    private async Task LockOut(User user)
    {
        await using var db = Postgres.CreateContext();
        await db.Users.Where(existing => existing.Id == user.Id).ExecuteUpdateAsync(set => set
            .SetProperty(existing => existing.FailedLoginAttempts, User.MaxFailedLogins)
            .SetProperty(existing => existing.LockedUntil, Now + User.LockoutDuration));
    }

    private async Task ShouldKeepTheirPassword(User user)
    {
        var saved = await Reload(user);
        saved.PasswordHash.Should().Be(user.PasswordHash);
        saved.MustChangePassword.Should().BeFalse();
    }
}
