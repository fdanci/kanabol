using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// The migration to several assignees on a database that had one per task (SPEC section 5.1): each task
/// keeps its assignee, assigned when the task last changed, or was made, by nobody known, and the
/// history of AssigneeId becomes that of Assignees. Each test runs on a database of its own in the same
/// container, without a pool, since a pooled connection to a database dropped before would be dead.
/// </summary>
public sealed class AddTaskAssigneesMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private const string Before = "20261106125110_AddTaskNumber";

    private static readonly Guid _role = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid _ana = Guid.Parse("00000000-0000-7000-8000-0000000000b1");
    private static readonly Guid _dan = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    private static readonly Guid _project = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid _column = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid _changed = Guid.Parse("00000000-0000-7000-8000-0000000000d1");
    private static readonly Guid _neverChanged = Guid.Parse("00000000-0000-7000-8000-0000000000d2");
    private static readonly Guid _nobodys = Guid.Parse("00000000-0000-7000-8000-0000000000d3");

    [Fact]
    public async Task Migrate_FromOneAssigneePerTask_KeepsEachAssigneeAndTurnsTheHistoryIntoAssignees()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Rows(db, "SELECT task_id, user_id, assigned_at, assigned_by_id IS NULL FROM task_assignees ORDER BY task_id")).Should().Equal(
            new object[][]
            {
                [_changed, _ana, new DateTime(2026, 11, 3, 9, 30, 0, DateTimeKind.Utc), true],
                [_neverChanged, _dan, new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc), true],
            },
            (actual, expected) => actual.SequenceEqual(expected));
        (await Rows(db, "SELECT column_name FROM information_schema.columns WHERE table_name = 'task_items' AND column_name = 'assignee_id'"))
            .Should().BeEmpty();
        (await Rows(db, "SELECT field, old_value, new_value FROM task_history_entries ORDER BY changed_at")).Should().Equal(
            new object[][]
            {
                ["Assignees", "", _dan.ToString()],
                ["Assignees", _ana.ToString(), ""],
                ["Title", "Vechi", "Al nimanui"],
                ["Assignees", _dan.ToString(), _ana.ToString()],
            },
            (actual, expected) => actual.SequenceEqual(expected));
    }

    [Fact]
    public async Task Migrate_BackFromSeveralAssignees_KeepsTheFirstOneAssignedOfEachTask()
    {
        await using var db = await AnOldDatabase();
        await db.GetService<IMigrator>().MigrateAsync();
        await Execute(db, $"INSERT INTO task_assignees (task_id, user_id, assigned_at) VALUES ('{_changed}', '{_dan}', '2026-11-05T00:00:00Z')");

        await db.GetService<IMigrator>().MigrateAsync(Before);

        (await Rows(db, "SELECT id, assignee_id FROM task_items ORDER BY number")).Should().Equal(
            new object[][] { [_changed, _ana], [_neverChanged, _dan], [_nobodys, DBNull.Value] },
            (actual, expected) => actual.SequenceEqual(expected));
        (await Rows(db, "SELECT field FROM task_history_entries ORDER BY changed_at")).Select(row => row[0])
            .Should().Equal("AssigneeId", "AssigneeId", "Title", "AssigneeId");
    }

    /// <summary>A database migrated up to the one before, with tasks that have an assignee or none, and their history.</summary>
    private async Task<AppDbContext> AnOldDatabase()
    {
        var database = $"migration_assignees_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        await db.GetService<IMigrator>().MigrateAsync(Before);
        await Execute(db, $"""
            INSERT INTO roles (id, name, is_system, holders_version, created_at) VALUES ('{_role}', 'Membru', false, 0, now());
            INSERT INTO users (id, email, display_name, password_hash, role_id, is_active, failed_login_attempts, must_change_password, created_at) VALUES
                ('{_ana}', 'ana@example.com', 'Ana', 'x', '{_role}', true, 0, false, now()),
                ('{_dan}', 'dan@example.com', 'Dan', 'x', '{_role}', false, 0, false, now());
            INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES ('{_project}', 'Vechi', 'OLD', 4, now());
            INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES ('{_column}', '{_project}', 'De făcut', 0, false, now());
            INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, assignee_id, created_at, updated_at) VALUES
                ('{_changed}', '{_project}', '{_column}', 1, 'Schimbat', 'Low', 0, '{_ana}', '2026-11-01T08:00:00Z', '2026-11-03T09:30:00Z'),
                ('{_neverChanged}', '{_project}', '{_column}', 2, 'Neschimbat', 'Low', 1, '{_dan}', '2026-11-02T10:00:00Z', NULL),
                ('{_nobodys}', '{_project}', '{_column}', 3, 'Al nimanui', 'Low', 2, NULL, '2026-11-02T11:00:00Z', NULL);
            INSERT INTO task_history_entries (id, task_id, field, old_value, new_value, changed_by_id, changed_at) VALUES
                (gen_random_uuid(), '{_changed}', 'AssigneeId', NULL, '{_dan}', '{_ana}', '2026-11-02T09:00:00Z'),
                (gen_random_uuid(), '{_changed}', 'AssigneeId', '{_dan}', '{_ana}', '{_ana}', '2026-11-03T09:30:00Z'),
                (gen_random_uuid(), '{_nobodys}', 'AssigneeId', '{_ana}', NULL, '{_ana}', '2026-11-02T11:30:00Z'),
                (gen_random_uuid(), '{_nobodys}', 'Title', 'Vechi', 'Al nimanui', '{_ana}', '2026-11-02T11:40:00Z');
            """);
        return db;
    }

    private static async Task Execute(AppDbContext db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, await Opened(db));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<NpgsqlConnection> Opened(AppDbContext db)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        return connection;
    }

    private static async Task<List<object[]>> Rows(AppDbContext db, string sql)
    {
        await using var command = new NpgsqlCommand(sql, await Opened(db));
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object[]>();
        while (await reader.ReadAsync())
        {
            var row = new object[reader.FieldCount];
            reader.GetValues(row);
            rows.Add(row);
        }

        return rows;
    }
}
