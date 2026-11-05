using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Roles.Queries.GetPermissions;
using Kanabol.Application.Features.Roles.Queries.GetRoleById;
using Kanabol.Application.Features.Roles.Queries.GetRoles;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Roles;

public sealed class RoleQueriesTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private IQueryHandler<GetRolesQuery, Page<RoleDto>> Roles => Get<IQueryHandler<GetRolesQuery, Page<RoleDto>>>();

    private IQueryHandler<GetRoleByIdQuery, RoleDto> RoleById => Get<IQueryHandler<GetRoleByIdQuery, RoleDto>>();

    private IQueryHandler<GetPermissionsQuery, string[]> AllPermissions => Get<IQueryHandler<GetPermissionsQuery, string[]>>();

    [Fact]
    public async Task GetRoles_WhenAllowed_ReturnsThemByNameWithPermissionsAndHolders()
    {
        var manager = await SignInWith(Permissions.RolesManage);
        await CreateRole("Zeta", Permissions.ReportsView, Permissions.TasksArchive);
        await CreateRole("Alfa");

        var result = await Roles.Handle(new GetRolesQuery(1, 50), CancellationToken.None);

        result.Value.TotalCount.Should().Be(3);
        result.Value.Number.Should().Be(1);
        result.Value.PageSize.Should().Be(50);
        result.Value.Items.Select(role => role.Name).Should().Equal("Alfa", $"Role of {manager.Email}", "Zeta");
        result.Value.Items[2].Permissions.Should().BeEquivalentTo(Permissions.ReportsView, Permissions.TasksArchive);
        result.Value.Items[1].UserCount.Should().Be(1);
        result.Value.Items[0].UserCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRoles_ForALaterPage_ReturnsWhatIsOnIt()
    {
        await SignInWith(Permissions.RolesManage);
        await CreateRole("Alfa");
        await CreateRole("Beta");
        await CreateRole("Gama");

        var result = await Roles.Handle(new GetRolesQuery(2, 2), CancellationToken.None);

        result.Value.TotalCount.Should().Be(4);
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].Name.Should().Be("Gama");
    }

    [Fact]
    public async Task GetRoles_ForAPagePastTheEnd_ReturnsNoItemsAndTheCount()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Roles.Handle(new GetRolesQuery(5, 50), CancellationToken.None);

        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(-1, 50, "Page")]
    [InlineData(int.MaxValue, 50, "Page")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public async Task GetRoles_WithAPageOutOfRange_ReturnsTheFieldsError(int page, int pageSize, string field)
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Roles.Handle(new GetRolesQuery(page, pageSize), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    // The largest allowed page, with the largest size, must not overflow when it becomes an offset.
    [Fact]
    public async Task GetRoles_OnTheLastPageAllowed_StillAnswers()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await Roles.Handle(new GetRolesQuery(Paging.MaximumPageNumber, Paging.MaximumPageSize), CancellationToken.None);

        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRoles_WithoutRolesManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await Roles.Handle(new GetRolesQuery(1, 50), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task GetRoleById_WhenAllowed_ReturnsTheRole()
    {
        await SignInWith(Permissions.RolesManage);
        var role = await CreateRole("Analist", Permissions.ReportsView);

        var result = await RoleById.Handle(new GetRoleByIdQuery(role.Id), CancellationToken.None);

        result.Value.Name.Should().Be("Analist");
        result.Value.Permissions.Should().BeEquivalentTo(Permissions.ReportsView);
    }

    [Fact]
    public async Task GetRoleById_WhenTheRoleDoesNotExist_ReturnsNotFound()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await RoleById.Handle(new GetRoleByIdQuery(Guid.CreateVersion7()), CancellationToken.None);

        result.Error.Should().Be(RoleErrors.NotFound);
    }

    [Fact]
    public async Task GetRoleById_WithoutRolesManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.UsersManage);
        var role = await CreateRole("Analist");

        var result = await RoleById.Handle(new GetRoleByIdQuery(role.Id), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task GetPermissions_WhenAllowed_ReturnsEveryPermission()
    {
        await SignInWith(Permissions.RolesManage);

        var result = await AllPermissions.Handle(new GetPermissionsQuery(), CancellationToken.None);

        result.Value.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task GetPermissions_WithoutRolesManage_ReturnsForbidden()
    {
        await SignInWith(Permissions.UsersManage);

        var result = await AllPermissions.Handle(new GetPermissionsQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.PermissionRequired);
    }
}
