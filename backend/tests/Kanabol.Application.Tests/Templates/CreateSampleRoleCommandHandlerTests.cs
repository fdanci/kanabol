using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Templates;

/// <summary>
/// The template for a handler's tests, on a real PostgreSQL: every command handler has at least
/// one test of success and one of refused access (SPEC section 11.1).
/// </summary>
public sealed class CreateSampleRoleCommandHandlerTests(PostgresFixture postgres) : HandlerTest(postgres)
{
    private ICommandHandler<CreateSampleRoleCommand, Guid> Handler => Get<ICommandHandler<CreateSampleRoleCommand, Guid>>();

    [Fact]
    public async Task Handle_WhenAllowed_SavesTheRoleWithItsAuditStamps()
    {
        var userId = Guid.CreateVersion7();
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        SignIn(userId, Permissions.RolesManage);
        Clock.UtcNow.Returns(now);

        var result = await Handler.Handle(new CreateSampleRoleCommand("Manager"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await using var db = Postgres.CreateContext();
        var role = await db.Roles.SingleAsync();
        role.Id.Should().Be(result.Value);
        role.Name.Should().Be("Manager");
        role.CreatedAt.Should().Be(now);
        role.CreatedById.Should().Be(userId);
    }

    [Fact]
    public async Task Handle_WhenUserLacksThePermission_ReturnsForbidden()
    {
        SignIn(Guid.CreateVersion7(), Permissions.ReportsView);

        var result = await Handler.Handle(new CreateSampleRoleCommand("Manager"), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Forbidden);
        await using var db = Postgres.CreateContext();
        (await db.Roles.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Handle_WhenTheNameIsTaken_ReturnsConflict()
    {
        SignIn(Guid.CreateVersion7(), Permissions.RolesManage);
        await Handler.Handle(new CreateSampleRoleCommand("Manager"), CancellationToken.None);

        var result = await Handler.Handle(new CreateSampleRoleCommand("Manager"), CancellationToken.None);

        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Handle_WhenTheNameIsEmpty_ReturnsTheFieldsErrorWithoutSaving()
    {
        SignIn(Guid.CreateVersion7(), Permissions.RolesManage);

        var result = await Handler.Handle(new CreateSampleRoleCommand(""), CancellationToken.None);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
        await using var db = Postgres.CreateContext();
        (await db.Roles.CountAsync()).Should().Be(0);
    }

    private void SignIn(Guid userId, params string[] permissions)
    {
        CurrentUser.IsAuthenticated.Returns(true);
        CurrentUser.Id.Returns(userId);
        CurrentUser.HasPermission(Arg.Is<string>(permission => permissions.Contains(permission))).Returns(true);
    }
}
