using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.DeleteRole;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Roles;

public sealed class DeleteRoleCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<DeleteRoleCommand, Unit> Handler => Get<ICommandHandler<DeleteRoleCommand, Unit>>();

    [Fact]
    public async Task Handle_WhenTheRoleIsUnused_DeletesItAndKeepsThePermissions()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist", Permissions.ReportsView);

        var result = await Handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await using var db = Postgres.CreateContext();
        (await db.Roles.AnyAsync(existing => existing.Id == role.Id)).Should().BeFalse();
        (await db.Permissions.CountAsync()).Should().Be(Permissions.All.Count);
    }

    [Fact]
    public async Task Handle_WhenTheRoleDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new DeleteRoleCommand(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutRolesManage_ReturnsForbiddenAndKeepsTheRole()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        await ShouldStillExist(role);
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");
        await Deactivate(manager);

        var result = await Handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        await ShouldStillExist(role);
    }

    [Fact]
    public async Task Handle_WhenDeletingAdmin_ReturnsConflictAndKeepsIt()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Handler.Handle(new DeleteRoleCommand(admin.RoleId), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.SystemRoleIsFixed);
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WhenSomeoneHasTheRole_ReturnsConflictAndKeepsIt()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");
        await AddHolder(role);

        var result = await Handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.InUse);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await ShouldStillExist(role);
    }

    // Users stay in the database when deactivated, and each of them needs a role.
    [Fact]
    public async Task Handle_WhenOnlyADeactivatedUserHasTheRole_ReturnsConflict()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");
        await Deactivate(await AddHolder(role));

        var result = await Handler.Handle(new DeleteRoleCommand(role.Id), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.InUse);
    }

    private async Task<Kanabol.Domain.Users.User> AddHolder(Role role)
    {
        await using var db = Postgres.CreateContext();
        var user = new UserBuilder().InRole(role).Build();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task ShouldStillExist(Role role)
    {
        await using var db = Postgres.CreateContext();
        (await db.Roles.AnyAsync(existing => existing.Id == role.Id)).Should().BeTrue();
    }
}
