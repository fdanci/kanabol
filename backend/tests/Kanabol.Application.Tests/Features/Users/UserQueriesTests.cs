using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Queries.GetAssignableRoles;
using Kanabol.Application.Features.Users.Queries.GetUserById;
using Kanabol.Application.Features.Users.Queries.GetUsers;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Users;

public sealed class UserQueriesTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private IQueryHandler<GetUsersQuery, Page<UserDto>> Users => Get<IQueryHandler<GetUsersQuery, Page<UserDto>>>();

    private IQueryHandler<GetUserByIdQuery, UserDto> UserById => Get<IQueryHandler<GetUserByIdQuery, UserDto>>();

    private IQueryHandler<GetAssignableRolesQuery, AssignableRoleDto[]> AssignableRoles =>
        Get<IQueryHandler<GetAssignableRolesQuery, AssignableRoleDto[]>>();

    [Fact]
    public async Task GetUsers_WhenAllowed_ReturnsThemByEmailIncludingTheDeactivated()
    {
        await SignInWith(Permissions.UsersManage);
        var deactivated = await CreateUser("zeta@example.com");
        await Deactivate(deactivated);
        await CreateUser("alfa@example.com");

        var result = await Users.Handle(new GetUsersQuery(1, 50), CancellationToken.None);

        result.Value.TotalCount.Should().Be(3);
        result.Value.Items.Select(user => user.Email).Should().Equal("alfa@example.com", "manager@example.com", "zeta@example.com");
        result.Value.Items[2].IsActive.Should().BeFalse();
        result.Value.Items[0].RoleName.Should().Be("Role of alfa@example.com");
    }

    [Fact]
    public async Task GetUsers_ForALaterPage_ReturnsWhatIsOnIt()
    {
        await SignInWith(Permissions.UsersManage);
        await CreateUser("alfa@example.com");
        await CreateUser("beta@example.com");

        var result = await Users.Handle(new GetUsersQuery(2, 2), CancellationToken.None);

        result.Value.Number.Should().Be(2);
        result.Value.Items.Should().ContainSingle().Which.Email.Should().Be("manager@example.com");
    }

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(1, 101, "PageSize")]
    public async Task GetUsers_WithAPageOutOfRange_ReturnsTheFieldsError(int page, int pageSize, string field)
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Users.Handle(new GetUsersQuery(page, pageSize), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task GetUsers_WithoutUsersManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Users.Handle(new GetUsersQuery(1, 50), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task GetUserById_WhenAllowed_ReturnsTheUser()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await UserById.Handle(new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.Value.Email.Should().Be(user.Email);
        result.Value.RoleId.Should().Be(user.RoleId);
    }

    [Fact]
    public async Task GetUserById_WhenTheUserDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await UserById.Handle(new GetUserByIdQuery(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(UserErrors.NotFound);
    }

    [Fact]
    public async Task GetUserById_WithoutUsersManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.RolesManage);
        var user = await CreateUser();

        var result = await UserById.Handle(new GetUserByIdQuery(user.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task GetAssignableRoles_ForACallerWithLess_LeavesOutWhatTheyCannotGive()
    {
        await SignInWith(Permissions.UsersManage, Permissions.ReportsView);
        await CreateAdmin();
        await CreateRole("Analist", Permissions.ReportsView);
        await CreateRole("Vizitator");
        await CreateRole("Sef", Permissions.UsersManage, Permissions.TasksArchive);

        var result = await AssignableRoles.Handle(new GetAssignableRolesQuery(), CancellationToken.None);

        result.Value.Select(role => role.Name).Should().Equal("Analist", "Role of manager@example.com", "Vizitator");
    }

    [Fact]
    public async Task GetAssignableRoles_ForAnAdmin_ListsEveryRole()
    {
        var admin = await CreateAdmin();
        SignIn(admin);
        await CreateRole("Sef", Permissions.UsersManage, Permissions.TasksArchive);

        var result = await AssignableRoles.Handle(new GetAssignableRolesQuery(), CancellationToken.None);

        result.Value.Select(role => role.Name).Should().Equal(Role.AdminName, "Sef");
    }

    [Fact]
    public async Task GetAssignableRoles_WithoutUsersManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await AssignableRoles.Handle(new GetAssignableRolesQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }
}
