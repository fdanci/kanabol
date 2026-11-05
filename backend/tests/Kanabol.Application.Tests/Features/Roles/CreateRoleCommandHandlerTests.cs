using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.CreateRole;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Roles;

public sealed class CreateRoleCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<CreateRoleCommand, RoleDto> Handler => Get<ICommandHandler<CreateRoleCommand, RoleDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_SavesTheRoleWithItsPermissions()
    {
        var manager = await SignInWith(Permissions.RolesManage, Permissions.ReportsView);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", [Permissions.ReportsView]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Analist");
        result.Value.IsSystem.Should().BeFalse();
        result.Value.Permissions.Should().BeEquivalentTo(Permissions.ReportsView);
        result.Value.UserCount.Should().Be(0);

        await using var db = Postgres.CreateContext();
        var role = await db.Roles.SingleAsync(existing => existing.Id == result.Value.Id);
        role.CreatedById.Should().Be(manager.Id);
        (await PermissionNamesOf(role.Id)).Should().BeEquivalentTo(Permissions.ReportsView);
    }

    [Fact]
    public async Task Handle_WithoutPermissions_SavesAnEmptyRole()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand("Vizitator", []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenAPermissionIsListedTwice_GrantsItOnce()
    {
        await SignInWith(Permissions.RolesManage, Permissions.ReportsView);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", [Permissions.ReportsView, Permissions.ReportsView]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await PermissionNamesOf(result.Value.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_WhenTheNameHasSpacesAround_KeepsItTrimmed()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand("  Analist  ", []), CancellationToken.None);

        result.Value.Name.Should().Be("Analist");
    }

    [Fact]
    public async Task Handle_WithoutRolesManage_ReturnsForbiddenWithoutSaving()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", []), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        await ShouldHaveNoRoleNamed("Analist");
    }

    // The token said yes and got the request this far: the database decides here.
    [Fact]
    public async Task Handle_WhenOnlyAnOldTokenHasRolesManage_ReturnsForbidden()
    {
        await SignInWith();
        CurrentUser.HasPermission(Permissions.RolesManage).Returns(true);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", []), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.RolesManage);
        await Deactivate(manager);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", []), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        await ShouldHaveNoRoleNamed("Analist");
    }

    // Otherwise roles.manage alone would be a way to any other permission.
    [Fact]
    public async Task Handle_WithAPermissionTheCallerDoesNotHave_ReturnsForbiddenWithoutSaving()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", [Permissions.UsersManage]), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.PermissionsBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await ShouldHaveNoRoleNamed("Analist");
    }

    [Fact]
    public async Task Handle_WhenTheNameIsTaken_ReturnsConflict()
    {
        await SignInWith(Permissions.RolesManage);
        await CreateRole("Analist");

        var result = await Handler.Handle(new CreateRoleCommand("Analist", []), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NameTaken);
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    // A second "admin" next to the system role would read as the same thing.
    [Theory]
    [InlineData("ADMIN")]
    [InlineData("admin")]
    [InlineData(" Admin ")]
    public async Task Handle_WhenTheNameDiffersOnlyInCaseOrSpaces_ReturnsConflict(string name)
    {
        await CreateAdmin();
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand(name, []), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NameTaken);
    }

    [Fact]
    public async Task Handle_WhenTwoRequestsCreateTheSameNameAtOnce_OneGetsConflict()
    {
        await SignInWith(Permissions.RolesManage);

        var results = await Task.WhenAll(CreateInOwnScope("Analist"), CreateInOwnScope("Analist"));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(RoleErrors.NameTaken);
    }

    [Fact]
    public async Task Handle_WithAnUnknownPermission_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand("Analist", ["no.such.permission"]), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().ContainSingle(key => key.StartsWith("Permissions", StringComparison.Ordinal));
        await ShouldHaveNoRoleNamed("Analist");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_WhenTheNameIsEmpty_ReturnsTheFieldsError(string name)
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand(name, []), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Fact]
    public async Task Handle_WhenTheNameIsTooLong_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Handler.Handle(new CreateRoleCommand(new string('x', 101), []), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    private async Task<Result<RoleDto>> CreateInOwnScope(string name)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateRoleCommand, RoleDto>>();
        return await handler.Handle(new CreateRoleCommand(name, []), CancellationToken.None);
    }

    private async Task ShouldHaveNoRoleNamed(string name)
    {
        await using var db = Postgres.CreateContext();
        (await db.Roles.AnyAsync(role => role.Name == name)).Should().BeFalse();
    }
}
