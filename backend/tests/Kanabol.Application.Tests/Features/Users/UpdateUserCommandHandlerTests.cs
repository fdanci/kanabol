using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.UpdateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class UpdateUserCommandHandlerTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<UpdateUserCommand, UserDto> Handler => Get<ICommandHandler<UpdateUserCommand, UserDto>>();

    [Fact]
    public async Task Handle_WhenAllowed_ChangesTheEmailAndTheName()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, " Ana.Noua@Example.com ", " Ana Noua "), CancellationToken.None);

        result.Value.Email.Should().Be("ana.noua@example.com");
        result.Value.DisplayName.Should().Be("Ana Noua");
        var saved = await Reload(user);
        saved.Email.Should().Be("ana.noua@example.com");
        saved.DisplayName.Should().Be("Ana Noua");
        saved.PasswordHash.Should().Be(user.PasswordHash);
        saved.RoleId.Should().Be(user.RoleId);
    }

    [Fact]
    public async Task Handle_WhenTheEmailBelongsToAnother_ReturnsConflict()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await CreateUser("alta@example.com");

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, "ALTA@example.com", "Ana Pop"), CancellationToken.None);

        result.Error.Should().Be(UserErrors.EmailTaken);
    }

    [Fact]
    public async Task Handle_WhenTheEmailStaysItsOwn_Succeeds()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, user.Email.ToUpperInvariant(), "Ana Noua"), CancellationToken.None);

        result.Value.Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task Handle_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Handler.Handle(new UpdateUserCommand(Guid.CreateVersion7(), "ana@example.com", "Ana"), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task Handle_WithoutUsersManage_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, "alta@example.com", "Alta"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
        (await Reload(user)).Email.Should().Be(user.Email);
    }

    [Fact]
    public async Task Handle_WhenTheCallerIsDeactivated_ReturnsSessionEnded()
    {
        var manager = await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();
        await Deactivate(manager);

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, "alta@example.com", "Alta"), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
    }

    // Changing the email of someone above you would lock them out, since it's what they sign in with.
    [Fact]
    public async Task Handle_WhenTheUserHasPermissionsTheCallerLacks_ReturnsForbiddenAndChangesNothing()
    {
        await SignInWith(Permissions.UsersManage);
        var admin = await CreateAdmin();

        var result = await Handler.Handle(new UpdateUserCommand(admin.Id, "alta@example.com", "Alta"), CancellationToken.None);

        result.Error.Should().Be(UserErrors.PermissionsBeyondYours);
        (await Reload(admin)).Email.Should().Be(admin.Email);
    }

    [Theory]
    [InlineData("nu-e-email", "Ana", "Email")]
    [InlineData("ana@example.com", " ", "DisplayName")]
    public async Task Handle_WithInvalidValues_ReturnsTheFieldsError(string email, string displayName, string field)
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Handler.Handle(new UpdateUserCommand(user.Id, email, displayName), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }
}
