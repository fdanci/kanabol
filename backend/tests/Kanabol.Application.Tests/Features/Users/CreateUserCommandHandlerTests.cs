using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.CreateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class CreateUserCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private const string InitialPassword = "o parola initiala buna";

    private ICommandHandler<CreateUserCommand, UserDto> Handler => Get<ICommandHandler<CreateUserCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_SavesAnActiveUserWhoMustChangeThePassword()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(Command("Ana.Pop@Example.com", role.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Email.Should().Be("ana.pop@example.com");
        result.Value.DisplayName.Should().Be("Ana Pop");
        result.Value.RoleId.Should().Be(role.Id);
        result.Value.RoleName.Should().Be("Analist");
        result.Value.IsActive.Should().BeTrue();
        result.Value.MustChangePassword.Should().BeTrue();
        result.Value.CreatedAt.Should().Be(Now);

        await using var db = Postgres.CreateContext();
        var saved = await db.Users.SingleAsync(user => user.Id == result.Value.Id);
        saved.MustChangePassword.Should().BeTrue();
        saved.CreatedById.Should().Be(manager.Id);
        saved.PasswordHash.Should().NotContain(InitialPassword);
        Get<IPasswordHasher>().Verify(saved.PasswordHash, InitialPassword).Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenAllowed_NeverLogsThePassword()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        await Handler.Handle(Command("ana.pop@example.com", role.Id), CancellationToken.None);

        Logs.Entries.Should().NotContain(entry => entry.Message.Contains(InitialPassword, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handle_WhenTheEmailIsTaken_ReturnsConflict()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");
        await CreateUser("ana.pop@example.com");

        var result = await Handler.Handle(Command("ANA.POP@example.com", role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.EmailTaken);
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WhenTwoRequestsCreateTheSameEmailAtOnce_OneGetsConflict()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var results = await Task.WhenAll(CreateInOwnScope(role.Id), CreateInOwnScope(role.Id));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(UserErrors.EmailTaken);
    }

    [Fact]
    public async Task Handle_WhenTheRoleDoesNotExist_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(Command("ana.pop@example.com", Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(UserErrors.RoleNotFound);
        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("RoleId");
    }

    // Otherwise users.manage alone would be a way to any permission, by making yourself a new account.
    [Fact]
    public async Task Handle_WithARoleThatHasMoreThanTheCaller_ReturnsForbiddenWithoutSaving()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Sef", Permissions.UsersManage, Permissions.ReportsView);

        var result = await Handler.Handle(Command("ana.pop@example.com", role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.RoleBeyondYours);
        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await ShouldHaveNoUser("ana.pop@example.com");
    }

    [Fact]
    public async Task Handle_WhenACallerWithoutEveryPermissionGivesTheAdminRole_ReturnsForbidden()
    {
        var admin = await CreateAdmin();
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(Command("ana.pop@example.com", admin.RoleId), CancellationToken.None);

        result.Error.Should().Be(UserErrors.RoleBeyondYours);
    }

    [Fact]
    public async Task Handle_WhenAnAdminGivesTheAdminRole_Succeeds()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Handler.Handle(Command("ana.pop@example.com", admin.RoleId), CancellationToken.None);

        result.Value.RoleName.Should().Be(Role.AdminName);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenWithoutSaving()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(Command("ana.pop@example.com", role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        await ShouldHaveNoUser("ana.pop@example.com");
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");
        await Deactivate(manager);

        var result = await Handler.Handle(Command("ana.pop@example.com", role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
        await ShouldHaveNoUser("ana.pop@example.com");
    }

    [Theory]
    [InlineData("scurta")]
    [InlineData("password123")]
    public async Task Handle_WithAPasswordThePolicyRefuses_ReturnsTheFieldsError(string password)
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new CreateUserCommand("ana.pop@example.com", "Ana Pop", role.Id, password), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("InitialPassword");
    }

    [Theory]
    [InlineData("")]
    [InlineData("nu-e-email")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana pop@example.com")]
    [InlineData("ana@pop@example.com")]
    public async Task Handle_WithAnInvalidEmail_ReturnsTheFieldsError(string email)
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(Command(email, role.Id), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public async Task Handle_WithAnEmailLongerThan320_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(Command(new string('a', 309) + "@example.com", role.Id), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Email");
    }

    [Fact]
    public async Task Handle_WithAnEmailOf320_Succeeds()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(Command(new string('a', 308) + "@example.com", role.Id), CancellationToken.None);

        result.Value.Email.Should().HaveLength(320);
    }

    [Fact]
    public async Task Handle_WithADisplayNameLongerThan200_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new CreateUserCommand("ana@example.com", new string('A', 201), role.Id, InitialPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("DisplayName");
    }

    [Fact]
    public async Task Handle_WithAnEmptyDisplayName_ReturnsTheFieldsError()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Handler.Handle(new CreateUserCommand("ana.pop@example.com", "  ", role.Id, InitialPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("DisplayName");
    }

    private static CreateUserCommand Command(string email, Guid roleId) => new(email, "Ana Pop", roleId, InitialPassword);

    private async Task<Result<UserDto>> CreateInOwnScope(Guid roleId)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateUserCommand, UserDto>>();
        return await handler.Handle(Command("ana.pop@example.com", roleId), CancellationToken.None);
    }

    private async Task ShouldHaveNoUser(string email)
    {
        await using var db = Postgres.CreateContext();
        (await db.Users.AnyAsync(user => user.Email == email)).Should().BeFalse();
    }
}
