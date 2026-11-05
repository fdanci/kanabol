using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Commands.LoginUser;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Commands.CreateRole;
using Kanabol.Application.Features.Roles.Commands.UpdateRole;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.CreateUser;
using Kanabol.Application.Features.Users.Commands.UpdateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Users;

/// <summary>
/// Characters nobody types: PostgreSQL refuses NUL in text, which made a 500 out of a bad field, and the
/// invisible ones make two names or two emails that look the same, or a name that looks empty.
/// </summary>
public sealed class HiddenCharactersTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private const string InitialPassword = "o parola initiala buna";

    public static TheoryData<string> HiddenInEmails => new()
    {
        "a\u0000@example.com",
        "a​@example.com",
        "﻿a@example.com",
        "a@example.com​",
        "a\t@example.com",
    };

    public static TheoryData<string> HiddenInNames => new()
    {
        "A\u0000na",
        "​",
        "Admin​",
        "﻿Admin",
        "A\u0007na",
        "Ana\r\nPop",
    };

    [Theory]
    [MemberData(nameof(HiddenInEmails))]
    public async Task CreateUser_WithAHiddenCharacterInTheEmail_ReturnsTheFieldsError(string email)
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Get<ICommandHandler<CreateUserCommand, UserDto>>()
            .Handle(new CreateUserCommand(email, "Ana", role.Id, InitialPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Email");
    }

    [Theory]
    [MemberData(nameof(HiddenInNames))]
    public async Task CreateUser_WithAHiddenCharacterInTheName_ReturnsTheFieldsError(string displayName)
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Get<ICommandHandler<CreateUserCommand, UserDto>>()
            .Handle(new CreateUserCommand("ana@example.com", displayName, role.Id, InitialPassword), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("DisplayName");
    }

    [Theory]
    [MemberData(nameof(HiddenInEmails))]
    public async Task UpdateUser_WithAHiddenCharacterInTheEmail_ReturnsTheFieldsError(string email)
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Get<ICommandHandler<UpdateUserCommand, UserDto>>()
            .Handle(new UpdateUserCommand(user.Id, email, "Ana"), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Email");
    }

    [Theory]
    [MemberData(nameof(HiddenInNames))]
    public async Task UpdateUser_WithAHiddenCharacterInTheName_ReturnsTheFieldsError(string displayName)
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Get<ICommandHandler<UpdateUserCommand, UserDto>>()
            .Handle(new UpdateUserCommand(user.Id, user.Email, displayName), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("DisplayName");
    }

    [Theory]
    [MemberData(nameof(HiddenInNames))]
    public async Task CreateRole_WithAHiddenCharacterInTheName_ReturnsTheFieldsError(string name)
    {
        await CreateAdmin();
        await SignInWith(Permissions.RolesManage);

        var result = await Get<ICommandHandler<CreateRoleCommand, RoleDto>>()
            .Handle(new CreateRoleCommand(name, []), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    [Theory]
    [MemberData(nameof(HiddenInNames))]
    public async Task UpdateRole_WithAHiddenCharacterInTheName_ReturnsTheFieldsError(string name)
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist");

        var result = await Get<ICommandHandler<UpdateRoleCommand, RoleDto>>()
            .Handle(new UpdateRoleCommand(role.Id, name, []), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    // Anyone can send this one, signed in or not.
    [Theory]
    [MemberData(nameof(HiddenInEmails))]
    public async Task Login_WithAHiddenCharacterInTheEmail_ReturnsTheFieldsError(string email)
    {
        var result = await Get<ICommandHandler<LoginUserCommand, AuthSession>>()
            .Handle(new LoginUserCommand(email, Password), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Email");
    }

    // An ordinary name still goes through: the rule is about what nobody sees, not about diacritics.
    [Theory]
    [InlineData("Ștefan Țepeș")]
    [InlineData("Ana-Maria O'Neil")]
    public async Task CreateUser_WithAnOrdinaryName_Succeeds(string displayName)
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await Get<ICommandHandler<CreateUserCommand, UserDto>>()
            .Handle(new CreateUserCommand("ana@example.com", displayName, role.Id, InitialPassword), CancellationToken.None);

        result.Value.DisplayName.Should().Be(displayName);
    }
}
