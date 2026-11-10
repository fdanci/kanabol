using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// The work schedule's one row (SPEC sections 4.4 and 5.1, group 22): the migration puts it, Monday to Friday,
/// 08:30 to 16:30, under its fixed id, and a second row can't come. On a database of its own, as the other
/// migration tests, since the tests empty every table before each.
/// </summary>
public sealed class AddWorkScheduleMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    [Fact]
    public async Task Migrate_PutsTheDefaultScheduleAsTheOneRow()
    {
        await using var db = await ANewDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        var rows = await db.WorkSchedules.AsNoTracking().ToListAsync();
        rows.Should().ContainSingle();
        rows[0].Should().BeEquivalentTo(new
        {
            Id = Domain.Settings.WorkSchedule.TheId,
            WorkDays = "1,2,3,4,5",
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(16, 30),
            UpdatedAt = (DateTimeOffset?)null,
            UpdatedById = (Guid?)null,
        });
        var second = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO work_schedules (id, work_days, start_time, end_time) VALUES ('00000000-0000-7000-8000-000000000001', '1', '09:00', '10:00')");
        (await second.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("pk_work_schedules");
    }

    [Fact]
    public async Task Migrate_Back_DropsTheTable()
    {
        await using var db = await ANewDatabase();
        await db.GetService<IMigrator>().MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261110222750_AddTaskPauseReason");

        var tables = await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM information_schema.tables WHERE table_name = 'work_schedules'").SingleAsync();
        tables.Should().Be(0);
    }

    private async Task<AppDbContext> ANewDatabase()
    {
        var database = $"migration_schedule_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
    }
}
