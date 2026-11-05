using Kanabol.Application.Common;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.CreateRole;
using Kanabol.Application.Features.Roles.Commands.DeleteRole;
using Kanabol.Application.Features.Roles.Commands.UpdateRole;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.CreateUser;
using Kanabol.Application.Features.Users.Commands.UpdateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Roles;

/// <summary>
/// Each request stops just before its save, while another one changes what it checked. What the
/// database refuses then has to come back as the answer the check would have given, never as a 500.
/// </summary>
public sealed class ConcurrentRoleChangesTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private const string InitialPassword = "o parola initiala buna";

    // The check in memory ignores case, and only the index on lower(name) can see a request in flight.
    [Fact]
    public async Task CreateRole_WhenTheSameNameInAnotherCaseIsCreatedMeanwhile_ReturnsNameTaken()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateRoleCommand, RoleDto>(new CreateRoleCommand("Analist", [])),
            async () => (await HandleInOwnScope<CreateRoleCommand, RoleDto>(new CreateRoleCommand("ANALIST", []))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NameTaken);
        await using var db = Postgres.CreateContext();
        (await db.Roles.Select(role => role.Name).ToListAsync()).Count(name => string.Equals(name, "analist", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public async Task UpdateRole_WhenItsNewNameIsTakenMeanwhile_ReturnsNameTaken()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Programator");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateRoleCommand, RoleDto>(new UpdateRoleCommand(role.Id, "Analist", [])),
            async () => (await HandleInOwnScope<CreateRoleCommand, RoleDto>(new CreateRoleCommand("analist", []))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NameTaken);
    }

    [Fact]
    public async Task UpdateRole_WhenTheRoleIsDeletedMeanwhile_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateRoleCommand, RoleDto>(new UpdateRoleCommand(role.Id, "Analist", [])),
            async () => (await HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task UpdateRole_ThatRenamesARoleDeletedMeanwhile_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateRoleCommand, RoleDto>(new UpdateRoleCommand(role.Id, "Analist senior", [])),
            async () => (await HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task UpdateRole_ThatGrantsToARoleDeletedMeanwhile_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage, Permissions.ReportsView);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateRoleCommand, RoleDto>(new UpdateRoleCommand(role.Id, "Analist", [Permissions.ReportsView])),
            async () => (await HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task DeleteRole_WhenItIsDeletedMeanwhile_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id)),
            async () => (await HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task DeleteRole_WhenSomeoneGetsItMeanwhile_ReturnsInUse()
    {
        await SignInWith(Permissions.RolesManage, Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id)),
            async () => (await HandleInOwnScope<CreateUserCommand, UserDto>(new CreateUserCommand("ana@example.com", "Ana", role.Id, InitialPassword))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(RoleErrors.InUse);
    }

    [Fact]
    public async Task CreateUser_WhenTheRoleIsDeletedMeanwhile_ReturnsRoleNotFound()
    {
        await SignInWith(Permissions.RolesManage, Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<CreateUserCommand, UserDto>(new CreateUserCommand("ana@example.com", "Ana", role.Id, InitialPassword)),
            async () => (await HandleInOwnScope<DeleteRoleCommand, Unit>(new DeleteRoleCommand(role.Id))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.RoleNotFound);
    }

    [Fact]
    public async Task UpdateUser_WhenTheEmailIsTakenMeanwhile_ReturnsEmailTaken()
    {
        await SignInWith(Permissions.UsersManage);
        var first = await CreateUser("first@example.com");
        var second = await CreateUser("second@example.com");

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<UpdateUserCommand, UserDto>(new UpdateUserCommand(first.Id, "noua@example.com", "Ana")),
            async () => (await HandleInOwnScope<UpdateUserCommand, UserDto>(new UpdateUserCommand(second.Id, "noua@example.com", "Ana"))).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(UserErrors.EmailTaken);
    }
}
