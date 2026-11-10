using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// The worked time's columns (SPEC sections 4.4 and 5.1, group 22): every task has worked nothing yet, and the
/// counter runs only in În lucru, so an open task already there starts counting when the migration runs; an
/// archived one, or one in another column, doesn't. On a database of its own, as the other migration tests.
/// </summary>
public sealed class AddWorkedTimeMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private const string Before = "20261110224413_AddWorkSchedule";
    private const string Project = "00000000-0000-7000-8000-000000000001";
    private const string Backlog = "00000000-0000-7000-8000-0000000000c0";
    private const string InProgress = "00000000-0000-7000-8000-0000000000c1";
    private const string Paused = "00000000-0000-7000-8000-0000000000c2";
    private const string Done = "00000000-0000-7000-8000-0000000000c3";

    [Fact]
    public async Task Migrate_StartsTheCounterOfTheOpenTasksInÎnLucruOnly()
    {
        await using var db = await AnOldDatabase();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        await db.GetService<IMigrator>().MigrateAsync();

        var tasks = await db.TaskItems.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(task => task.Number)
            .Select(task => new { task.Title, task.WorkedSeconds, task.WorkStartedAt })
            .ToListAsync();
        tasks.Should().AllSatisfy(task => task.WorkedSeconds.Should().Be(0));
        tasks.Where(task => task.WorkStartedAt is not null).Select(task => task.Title).Should().Equal("in progress");
        tasks.Single(task => task.Title == "in progress").WorkStartedAt.Should().BeAfter(before).And.BeBefore(DateTimeOffset.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Migrate_Back_DropsBothColumns()
    {
        await using var db = await AnOldDatabase();
        await db.GetService<IMigrator>().MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(Before);

        var columns = await db.Database.SqlQueryRaw<long>(
                "SELECT count(*) AS \"Value\" FROM information_schema.columns WHERE table_name = 'task_items' AND column_name IN ('worked_seconds', 'work_started_at')")
            .SingleAsync();
        columns.Should().Be(0);
        (await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM task_items").SingleAsync()).Should().Be(5);
    }

    private async Task<AppDbContext> AnOldDatabase()
    {
        var database = $"migration_worked_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        await db.GetService<IMigrator>().MigrateAsync(Before);

        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES ('{Project}', 'Timp', 'TIM', 6, now());
            INSERT INTO board_columns (id, project_id, name, position, kind, created_at) VALUES
                ('{Backlog}', '{Project}', 'Backlog', 0, 'Backlog', now()),
                ('{InProgress}', '{Project}', 'În lucru', 1, 'InProgress', now()),
                ('{Paused}', '{Project}', 'În pauză', 2, 'Paused', now()),
                ('{Done}', '{Project}', 'Gata', 3, 'Done', now());
            INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at, completed_at, archived_at, pause_reason) VALUES
                (gen_random_uuid(), '{Project}', '{Backlog}', 1, 'in backlog', 'Low', 0, now(), NULL, NULL, NULL),
                (gen_random_uuid(), '{Project}', '{InProgress}', 2, 'in progress', 'Low', 0, now(), NULL, NULL, NULL),
                (gen_random_uuid(), '{Project}', '{InProgress}', 3, 'in progress, archived', 'Low', 1, now(), NULL, now(), NULL),
                (gen_random_uuid(), '{Project}', '{Paused}', 4, 'paused', 'Low', 0, now(), NULL, NULL, 'Aștept'),
                (gen_random_uuid(), '{Project}', '{Done}', 5, 'done', 'Low', 0, now(), now(), NULL, NULL);
            """);
        return db;
    }
}
