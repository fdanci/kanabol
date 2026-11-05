using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Auth.Queries.GetCurrentUser;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Auth;

public sealed class GetCurrentUserQueryHandlerTests(PostgresFixture postgres) : AuthHandlerTest(postgres)
{
    private IQueryHandler<GetCurrentUserQuery, CurrentUserDto> Handler => Get<IQueryHandler<GetCurrentUserQuery, CurrentUserDto>>();

    [Fact]
    public async Task Handle_WhenSignedIn_ReturnsTheUserAndTheRolesPermissions()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage, Permissions.ReportsView]);
        await CreateUser("other@example.com", permissions: [Permissions.TasksArchive]);
        SignIn(user.Id);

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(user.Id);
        result.Value.Email.Should().Be("ana@example.com");
        result.Value.DisplayName.Should().Be("Ana Pop");
        result.Value.RoleName.Should().Be("Role of ana@example.com");
        result.Value.MustChangePassword.Should().BeFalse();
        result.Value.Permissions.Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
    }

    [Fact]
    public async Task Handle_WhenThePasswordMustChange_SaysSo()
    {
        var user = await CreateUser(mustChangePassword: true);
        SignIn(user.Id);

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Value.MustChangePassword.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTheRoleHasNoPermissions_ReturnsAnEmptyList()
    {
        var user = await CreateUser();
        SignIn(user.Id);

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Value.Permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenTheRolesPermissionsChanged_ReturnsTheCurrentOnes()
    {
        var user = await CreateUser(permissions: [Permissions.UsersManage]);
        SignIn(user.Id);
        await using (var db = Postgres.CreateContext())
        {
            var role = await db.Roles.Include(existing => existing.RolePermissions).SingleAsync(existing => existing.Id == user.RoleId);
            var added = new Permission(Permissions.ReportsView);
            db.Permissions.Add(added);
            role.Grant(added.Id);
            await db.SaveChangesAsync();
        }

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Value.Permissions.Should().BeEquivalentTo(Permissions.UsersManage, Permissions.ReportsView);
    }

    [Fact]
    public async Task Handle_WhenTheUserIsDeactivated_ReturnsSessionEnded()
    {
        var user = await CreateUser();
        await Deactivate(user);
        SignIn(user.Id);

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
    }

    [Fact]
    public async Task Handle_WhenTheUserNoLongerExists_ReturnsSessionEnded()
    {
        SignIn(Guid.CreateVersion7());

        var result = await Handler.Handle(new GetCurrentUserQuery(), CancellationToken.None);

        result.Error.Should().Be(AuthErrors.SessionEnded);
    }

    private void SignIn(Guid userId)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.Id.Returns(userId);
    }
}
