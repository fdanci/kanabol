using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Users;
using Kanabol.Application.Features.Users.Commands.ChangeUserRole;
using Kanabol.Application.Features.Users.Commands.DeactivateUser;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Features.Users;

/// <summary>SPEC section 4.4: the last user with the Admin role can't get another role or be deactivated.</summary>
public sealed class LastAdminTests(PostgresFixture postgres) : ManagementHandlerTest(postgres)
{
    private ICommandHandler<DeactivateUserCommand, UserDto> Deactivation => Get<ICommandHandler<DeactivateUserCommand, UserDto>>();

    private ICommandHandler<ChangeUserRoleCommand, UserDto> RoleChange => Get<ICommandHandler<ChangeUserRoleCommand, UserDto>>();

    [Fact]
    public async Task Deactivate_OfTheLastAdmin_ReturnsConflictAndChangesNothing()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await Deactivation.Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        (await Reload(admin)).IsActive.Should().BeTrue();
    }

    // Only someone with the Admin role counts as the admin who remains, not any active user.
    [Fact]
    public async Task Deactivate_OfTheLastAdminWhileOthersAreActive_ReturnsConflict()
    {
        var admin = await CreateAdmin();
        await CreateUser("ana@example.com");
        SignIn(admin);

        var result = await Deactivation.Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
    }

    [Fact]
    public async Task ChangeRole_OfTheLastAdminWhileOthersAreActive_ReturnsConflict()
    {
        var admin = await CreateAdmin();
        await CreateUser("ana@example.com");
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(admin.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
    }

    [Fact]
    public async Task Deactivate_OfAnAdminWhenAnotherIsActive_Succeeds()
    {
        var admin = await CreateAdmin();
        var other = await CreateAdmin("other.admin@example.com");
        SignIn(admin);

        var result = await Deactivation.Handle(new DeactivateUserCommand(other.Id), CancellationToken.None);

        result.Value.IsActive.Should().BeFalse();
    }

    // A deactivated admin can't sign in, so they don't count as the admin who remains.
    [Fact]
    public async Task Deactivate_WhenTheOnlyOtherAdminIsDeactivated_ReturnsConflict()
    {
        var admin = await CreateAdmin();
        await Deactivate(await CreateAdmin("other.admin@example.com"));
        SignIn(admin);

        var result = await Deactivation.Handle(new DeactivateUserCommand(admin.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
    }

    [Fact]
    public async Task Deactivate_OfTwoAdminsOneAfterTheOther_StopsAtTheLast()
    {
        var first = await CreateAdmin();
        var second = await CreateAdmin("second.admin@example.com");
        SignIn(first);

        (await Deactivation.Handle(new DeactivateUserCommand(second.Id), CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await Deactivation.Handle(new DeactivateUserCommand(first.Id), CancellationToken.None)).Error.Should().Be(UserErrors.LastAdmin);
    }

    [Fact]
    public async Task Deactivate_OfTheLastHolderOfAnotherRole_Succeeds()
    {
        await SignInWith(Permissions.UsersManage);
        var user = await CreateUser();

        var result = await Deactivation.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeRole_OfTheLastAdmin_ReturnsConflictAndChangesNothing()
    {
        var admin = await CreateAdmin();
        await AddLiveToken(admin);
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(admin.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
        (await Reload(admin)).RoleId.Should().Be(admin.RoleId);
        (await RefreshTokensOf(admin)).Should().OnlyContain(token => !token.IsRevoked);
    }

    [Fact]
    public async Task ChangeRole_OfAnAdminWhenAnotherIsActive_Succeeds()
    {
        var admin = await CreateAdmin();
        var other = await CreateAdmin("other.admin@example.com");
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(other.Id, role.Id), CancellationToken.None);

        result.Value.RoleId.Should().Be(role.Id);
    }

    [Fact]
    public async Task ChangeRole_WhenTheOnlyOtherAdminIsDeactivated_ReturnsConflict()
    {
        var admin = await CreateAdmin();
        await Deactivate(await CreateAdmin("other.admin@example.com"));
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(admin.Id, role.Id), CancellationToken.None);

        result.Error.Should().Be(UserErrors.LastAdmin);
    }

    // They aren't an admin anyone counts on, so moving them doesn't take anything away.
    [Fact]
    public async Task ChangeRole_OfADeactivatedAdmin_Succeeds()
    {
        var admin = await CreateAdmin();
        var deactivated = await CreateAdmin("other.admin@example.com");
        await Deactivate(deactivated);
        var role = await CreateRole("Analist");
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(deactivated.Id, role.Id), CancellationToken.None);

        result.Value.RoleId.Should().Be(role.Id);
    }

    // Nothing in the application leads here, but someone fixing a database by hand must not be stopped.
    [Fact]
    public async Task ChangeRole_OfADeactivatedAdminWhenNoActiveOneExists_Succeeds()
    {
        await Deactivate(await CreateAdmin());
        var deactivated = await CreateAdmin("other.admin@example.com");
        await Deactivate(deactivated);
        await SignInWith([.. Permissions.All]);
        var role = await CreateRole("Analist");

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(deactivated.Id, role.Id), CancellationToken.None);

        result.Value.RoleId.Should().Be(role.Id);
    }

    [Fact]
    public async Task ChangeRole_OfTheLastAdminToTheSameRole_Succeeds()
    {
        var admin = await CreateAdmin();
        SignIn(admin);

        var result = await RoleChange.Handle(new ChangeUserRoleCommand(admin.Id, admin.RoleId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_OfTheLastTwoAdminsAtTheSameMoment_LeavesOne()
    {
        var first = await CreateAdmin();
        var second = await CreateAdmin("second.admin@example.com");
        SignIn(first);

        var results = await WithBothPastTheirChecks(first.RoleId, () => Task.WhenAll(
            DeactivateInOwnScope(first.Id),
            DeactivateInOwnScope(second.Id)));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(UserErrors.ChangedMeanwhile);
        await ShouldHaveOneActiveAdmin(first.RoleId);
    }

    [Fact]
    public async Task Requests_ThatMoveAndDeactivateTheLastTwoAdminsAtOnce_LeaveOne()
    {
        var first = await CreateAdmin();
        var second = await CreateAdmin("second.admin@example.com");
        var role = await CreateRole("Analist");
        SignIn(first);

        var results = await WithBothPastTheirChecks(first.RoleId, () => Task.WhenAll(
            DeactivateInOwnScope(first.Id),
            MoveInOwnScope(second.Id, role.Id)));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(UserErrors.ChangedMeanwhile);
        await ShouldHaveOneActiveAdmin(first.RoleId);
    }

    [Fact]
    public async Task Requests_ThatMoveTheLastTwoAdminsAtOnce_LeaveOne()
    {
        var first = await CreateAdmin();
        var second = await CreateAdmin("second.admin@example.com");
        var role = await CreateRole("Analist");
        SignIn(first);

        var results = await WithBothPastTheirChecks(first.RoleId, () => Task.WhenAll(
            MoveInOwnScope(first.Id, role.Id),
            MoveInOwnScope(second.Id, role.Id)));

        results.Count(result => result.IsSuccess).Should().Be(1);
        results.Single(result => result.IsFailure).Error.Should().Be(UserErrors.ChangedMeanwhile);
        await ShouldHaveOneActiveAdmin(first.RoleId);
    }

    /// <summary>
    /// Holds the Admin role's row, so both requests get past every check and wait at their save, which
    /// is the moment one would overwrite what the other just decided. Reads aren't blocked by this, so
    /// nothing here depends on timing, and requests that don't touch the row just finish on their own.
    /// </summary>
    private async Task<Result<UserDto>[]> WithBothPastTheirChecks(Guid adminRoleId, Func<Task<Result<UserDto>[]>> requests)
    {
        await using var gate = Postgres.CreateContext();
        await using var transaction = await gate.Database.BeginTransactionAsync();
        await gate.Database.ExecuteSqlAsync($"select 1 from roles where id = {adminRoleId} for update");

        var both = requests();
        await WaitForTwoSavesOnTheLock(gate, both);
        await transaction.RollbackAsync();

        return await both;
    }

    private static async Task WaitForTwoSavesOnTheLock(AppDbContext gate, Task both)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!both.IsCompleted && DateTime.UtcNow < deadline)
        {
            var waiting = await gate.Database
                .SqlQuery<int>($"select count(*)::int as \"Value\" from pg_stat_activity where datname = current_database() and wait_event_type = 'Lock'")
                .SingleAsync();
            if (waiting >= 2)
            {
                return;
            }

            await Task.Delay(50);
        }
    }

    private async Task<Result<UserDto>> DeactivateInOwnScope(Guid userId)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeactivateUserCommand, UserDto>>();
        return await handler.Handle(new DeactivateUserCommand(userId), CancellationToken.None);
    }

    private async Task<Result<UserDto>> MoveInOwnScope(Guid userId, Guid roleId)
    {
        await using var scope = NewScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeUserRoleCommand, UserDto>>();
        return await handler.Handle(new ChangeUserRoleCommand(userId, roleId), CancellationToken.None);
    }

    private async Task ShouldHaveOneActiveAdmin(Guid adminRoleId)
    {
        await using var db = Postgres.CreateContext();
        (await db.Users.CountAsync(user => user.RoleId == adminRoleId && user.IsActive)).Should().Be(1);
    }

    private async Task AddLiveToken(User user)
    {
        await using var db = Postgres.CreateContext();
        db.RefreshTokens.Add(new RefreshToken(user.Id, Guid.CreateVersion7(), Guid.NewGuid().ToString("N"), Now.AddDays(7), Now));
        await db.SaveChangesAsync();
    }
}
