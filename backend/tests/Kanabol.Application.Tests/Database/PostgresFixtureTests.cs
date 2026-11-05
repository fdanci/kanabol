using Kanabol.Domain.Authorization;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Database;

public sealed class PostgresFixtureTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private const string SharedName = "only.one.of.these";

    [Fact]
    public async Task Database_WhenStarted_IsPostgres17()
    {
        await using var db = Postgres.CreateContext();

        var version = await db.Database.SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"").SingleAsync();

        version.Should().StartWith("17.");
    }

    [Fact]
    public async Task Database_WhenStarted_HasTheMigratedSchema()
    {
        await using var db = Postgres.CreateContext();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.TaskItems.CountAsync()).Should().Be(0);
    }

    // Both insert the same unique name, so whichever runs second fails if the reset doesn't work.
    [Fact]
    public Task Reset_BeforeOneTest_LeavesNoRowsBehind() => InsertTheOnlyRowAllowed();

    [Fact]
    public Task Reset_BeforeAnotherTest_LeavesNoRowsBehind() => InsertTheOnlyRowAllowed();

    [Fact]
    public async Task Reset_WhenRun_KeepsTheMigrationHistory()
    {
        await Postgres.ResetAsync();

        await using var db = Postgres.CreateContext();
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
    }

    private async Task InsertTheOnlyRowAllowed()
    {
        await using var db = Postgres.CreateContext();
        db.Permissions.Add(new Permission(SharedName));
        await db.SaveChangesAsync();

        (await db.Permissions.CountAsync()).Should().Be(1);
    }
}
