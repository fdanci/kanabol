using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.UpdateRole;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Roles;

public sealed class UpdateRoleCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<UpdateRoleCommand, RoleDto> Handler => Get<ICommandHandler<UpdateRoleCommand, RoleDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_RenamesAndReplacesThePermissions()
    {
        var manager = await SignInWith(Permissions.RolesManage, Permissions.ReportsView, Permissions.TasksArchive);
        var role = await CreateRole("Analist", Permissions.ReportsView);

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist senior", [Permissions.TasksArchive]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Analist senior");
        result.Value.Permissions.Should().BeEquivalentTo(Permissions.TasksArchive);

        await using var db = Postgres.CreateContext();
        var saved = await db.Roles.SingleAsync(existing => existing.Id == role.Id);
        saved.Name.Should().Be("Analist senior");
        saved.UpdatedById.Should().Be(manager.Id);
        (await PermissionNamesOf(role.Id)).Should().BeEquivalentTo(Permissions.TasksArchive);
    }

    [Fact]
    public async Task Handle_WhenTheRoleDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new UpdateRoleCommand(Guid.CreateVersion7(), "Analist", []), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutRolesManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Altceva", []), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        await using var db = Postgres.CreateContext();
        (await db.Roles.SingleAsync(existing => existing.Id == role.Id)).Name.Should().Be("Analist");
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");
        await Deactivate(manager);

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Altceva", []), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
    }

    [Fact]
    public async Task Handle_WhenRenamingAdmin_ReturnsConflictAndKeepsTheName()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Handler.Handle(new UpdateRoleCommand(admin.RoleId, "Sef", [.. Permissions.All]), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.SystemRoleIsFixed);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        await using var db = Postgres.CreateContext();
        (await db.Roles.SingleAsync(existing => existing.Id == admin.RoleId)).Name.Should().Be(Role.AdminName);
    }

    [Fact]
    public async Task Handle_WhenRestrictingAdmin_ReturnsConflictAndKeepsEveryPermission()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Handler.Handle(new UpdateRoleCommand(admin.RoleId, Role.AdminName, [Permissions.UsersManage]), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.SystemRoleIsFixed);
        (await PermissionNamesOf(admin.RoleId)).Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Handle_WhenTheNameIsTakenByAnotherRole_ReturnsConflict()
    {
        await SignInWith(Permissions.RolesManage);
        await CreateRole("Analist");
        var role = await CreateRole("Programator");

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "ANALIST", []), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NameTaken);
    }

    [Fact]
    public async Task Handle_WhenOnlyTheCaseOfItsOwnNameChanges_Succeeds()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("analist");

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist", []), CancellationToken.None);

        result.Value.Name.Should().Be("Analist");
    }

    [Fact]
    public async Task Handle_WhenAddingAPermissionTheCallerDoesNotHave_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist", [Permissions.UsersManage]), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.PermissionsBeyondYours);
        (await PermissionNamesOf(role.Id)).Should().BeEmpty();
    }

    // Only what's being added counts: editing a role doesn't require holding all it already has.
    [Fact]
    public async Task Handle_WhenKeepingAPermissionTheCallerDoesNotHave_Succeeds()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist", Permissions.ReportsView);

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist senior", [Permissions.ReportsView]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await PermissionNamesOf(role.Id)).Should().BeEquivalentTo(Permissions.ReportsView);
    }

    [Fact]
    public async Task Handle_WhenRemovingAPermissionTheCallerDoesNotHave_Succeeds()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist", Permissions.ReportsView);

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist", []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await PermissionNamesOf(role.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WithAnUnknownPermission_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new UpdateRoleCommand(role.Id, "Analist", ["no.such.permission"]), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Handle_WhenTwoRequestsEditTheSameRoleAtOnce_NeitherThrows()
    {
        await SignInWith(Permissions.RolesManage, Permissions.ReportsView);
        var role = await CreateRole("Analist");

        var results = await Task.WhenAll(
            UpdateInOwnScope(role.Id, [Permissions.ReportsView]),
            UpdateInOwnScope(role.Id, [Permissions.ReportsView]));

        results.Should().Contain(result => result.IsSuccess);
        results.Should().NotContain(result => result.IsFailure && result.Error != RoleErrors.ChangedMeanwhile);
        (await PermissionNamesOf(role.Id)).Should().BeEquivalentTo(Permissions.ReportsView);
    }

    private async Task<Result<RoleDto>> UpdateInOwnScope(Guid roleId, IReadOnlyList<string> permissions)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateRoleCommand, RoleDto>>();
        return await handler.Handle(new UpdateRoleCommand(roleId, "Analist", permissions), CancellationToken.None);
    }
}
