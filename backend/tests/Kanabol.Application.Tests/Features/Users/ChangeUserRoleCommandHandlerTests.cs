using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles.Commands.DeleteRole;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.ChangeUserRole;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class ChangeUserRoleCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<ChangeUserRoleCommand, UserDto> Handler => Get<ICommandHandler<ChangeUserRoleCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_ChangesTheRoleAndEndsTheUsersSessions()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var other = await CreateUser("other@example.com");
        await AddLiveToken(user);
        await AddLiveToken(user);
        await AddLiveToken(other);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None);

        result.Value.RoleId.Should().Be(role.Id);
        result.Value.RoleName.Should().Be("Analist");
        (await Reload(user)).RoleId.Should().Be(role.Id);
        (await RefreshTokensOf(user)).Should().OnlyContain(token => token.IsRevoked);
        (await RefreshTokensOf(other)).Should().OnlyContain(token => !token.IsRevoked);
    }

    [Fact]
    public async Task Handle_WhenTheRoleStaysTheSame_KeepsTheSessions()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await AddLiveToken(user);

        var result = await Handler.Handle(new ChangeUserRoleCommand(user.Id, user.RoleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await RefreshTokensOf(user)).Should().OnlyContain(token => !token.IsRevoked);
        await RealtimeAccess.DidNotReceive().DisconnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // Their boards reconnect with a fresh token, which their new role decides, or not at all (SPEC section 9).
    [Fact]
    public async Task Handle_WhenAllowed_ClosesOnlyThatUsersConnections()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var role = await CreateRole("Analist");

        (await Handler.Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();

        await RealtimeAccess.Received(1).DisconnectAsync(user.Id, Arg.Any<CancellationToken>());
        await RealtimeAccess.Received(1).DisconnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenRefused_ClosesNoConnection()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();
        var role = await CreateRole("Analist");

        (await Handler.Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None)).IsFailure.Should().BeTrue();

        await RealtimeAccess.DidNotReceive().DisconnectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new ChangeUserRoleCommand(Guid.CreateVersion7(), role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WhenTheRoleDoesNotExist_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new ChangeUserRoleCommand(user.Id, Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(UserErrors.RoleNotFound);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        (await Reload(user)).RoleId.Should().Be(user.RoleId);
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        var role = await CreateRole("Analist");
        await Deactivate(manager);

        var result = await Handler.Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
    }

    [Fact]
    public async Task Handle_WhenTheCallerGivesThemselvesTheAdminRole_ReturnsForbiddenAndChangesNothing()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var admin = await CreateAdmin();

        var result = await Handler.Handle(new ChangeUserRoleCommand(manager.Id, admin.RoleId), CancellationToken.None);

        result.Error.Should().Be(UserErrors.RoleBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        (await Reload(manager)).RoleId.Should().Be(manager.RoleId);
    }

    [Fact]
    public async Task Handle_WhenTheUserHasPermissionsTheCallerLacks_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        var admin = await CreateAdmin();
        await CreateAdmin("second.admin@example.com");
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new ChangeUserRoleCommand(admin.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.PermissionsBeyondYours);
        (await Reload(admin)).RoleId.Should().Be(admin.RoleId);
    }

    [Fact]
    public async Task Handle_WhenTheRoleIsDeletedAtTheSameMoment_NeitherRequestCrashes()
    {
        await SignInWith(Permissions.UsersManage, Permissions.RolesManage);
        var user = await CreateUser();
        var role = await CreateRole("Analist");

        await using var changeScope = NewScope();
        await using var deleteScope = NewScope();
        var results = await Task.WhenAll(
            changeScope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeUserRoleCommand, UserDto>>()
                .Handle(new ChangeUserRoleCommand(user.Id, role.Id), CancellationToken.None)
                .ContinueWith(task => (Result)task.Result, TaskScheduler.Default),
            deleteScope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteRoleCommand, Unit>>()
                .Handle(new DeleteRoleCommand(role.Id), CancellationToken.None)
                .ContinueWith(task => (Result)task.Result, TaskScheduler.Default));

        results.Count(result => result.IsSuccess).Should().Be(1);
    }

    private async Task AddLiveToken(User user)
    {
        await using var db = Postgres.CreateContext();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now));
        await db.SaveChangesAsync();
    }
}
