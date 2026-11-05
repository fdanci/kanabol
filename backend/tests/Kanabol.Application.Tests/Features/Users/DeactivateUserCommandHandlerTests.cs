using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.DeactivateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class DeactivateUserCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<DeactivateUserCommand, UserDto> Handler => Get<ICommandHandler<DeactivateUserCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_DeactivatesAndKeepsTheRow()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
        var saved = await Reload(user);
        saved.IsActive.Should().BeFalse();
        saved.Email.Should().Be(user.Email);
        saved.RoleId.Should().Be(user.RoleId);
    }

    [Fact]
    public async Task Handle_WhenAllowed_EndsOnlyThatUsersSessions()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        await AddLiveToken(user);
        await AddLiveToken(other);

        await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        (await RefreshTokensOf(user)).Should().OnlyContain(token => token.IsRevoked && token.RevokedAt == Now);
        (await RefreshTokensOf(other)).Should().OnlyContain(token => !token.IsRevoked);
    }

    [Fact]
    public async Task Handle_WhenAlreadyDeactivated_SucceedsAgain()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(user);

        var result = await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(new DeactivateUserCommand(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        (await Reload(user)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(manager);

        var result = await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        (await Reload(user)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheUserHasPermissionsTheCallerLacks_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        var admin = await CreateAdmin();
        await CreateAdmin("second.admin@example.com");

        var result = await Handler.Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.PermissionsBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        (await Reload(admin)).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDeactivating_LeavesOtherUsersAlone()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");

        await Handler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        await using var db = Postgres.CreateContext();
        (await db.Users.SingleAsync(existing => existing.Id == other.Id)).IsActive.Should().BeTrue();
    }

    private async Task AddLiveToken(User user)
    {
        await using var db = Postgres.CreateContext();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now));
        await db.SaveChangesAsync();
    }
}
