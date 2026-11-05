using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.ActivateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class ActivateUserCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<ActivateUserCommand, UserDto> Handler => Get<ICommandHandler<ActivateUserCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_ActivatesAndLeavesTheRestAlone()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser(mustChangePassword: true);
        await Deactivate(user);

        var result = await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
        var saved = await Reload(user);
        saved.IsActive.Should().BeTrue();
        saved.Email.Should().Be(user.Email);
        saved.RoleId.Should().Be(user.RoleId);
        saved.PasswordHash.Should().Be(user.PasswordHash);
        saved.MustChangePassword.Should().BeTrue();
    }

    // The sessions ended with the deactivation, and coming back doesn't bring them back.
    [Fact]
    public async Task Handle_WhenAllowed_LeavesTheEndedSessionsEnded()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await AddRevokedToken(user);
        await Deactivate(user);

        await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        (await RefreshTokensOf(user)).Should().ContainSingle().Which.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAllowed_LogsIt()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(user);

        await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        Logs.Entries.Should().Contain(entry => entry.Message.Contains("reactivated", StringComparison.Ordinal) && entry.Message.Contains(user.Id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_WhenAlreadyActive_SucceedsAgain()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
        (await Reload(user)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(new ActivateUserCommand(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();
        await Deactivate(user);

        var result = await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        (await Reload(user)).IsActive.Should().BeFalse();
    }

    // A dismissed manager mustn't be able to bring themselves back with the token they still hold.
    [Fact]
    public async Task Handle_WhenTheCallerActivatesThemselvesAfterBeingDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        await Deactivate(manager);

        var result = await Handler.Handle(new ActivateUserCommand(manager.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        (await Reload(manager)).IsActive.Should().BeFalse();
    }

    // Otherwise users.manage could bring back an admin account, and with a password reset, sign in as it.
    [Fact]
    public async Task Handle_WhenTheUserHasPermissionsTheCallerLacks_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        await CreateAdmin();
        var admin = await CreateAdmin("second.admin@example.com");
        await Deactivate(admin);

        var result = await Handler.Handle(new ActivateUserCommand(admin.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.PermissionsBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        (await Reload(admin)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTheUserIsInARoleTheCallerCovers_Succeeds()
    {
        await SignInWith(Permissions.UsersManage, Permissions.ReportsView);
        var user = await CreateUser("analist@example.com", permissions: Permissions.ReportsView);
        await Deactivate(user);

        var result = await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenActivating_LeavesOtherUsersAlone()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        await Deactivate(user);
        await Deactivate(other);

        await Handler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);

        (await Reload(other)).IsActive.Should().BeFalse();
    }

    private async Task AddRevokedToken(User user)
    {
        await using var db = Postgres.CreateContext();
        var token = new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now);
        token.Revoke(Now);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
    }
}
