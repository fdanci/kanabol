using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// The ro-x-icu collation on the text columns the lists sort by (SPEC section 5.3, group 20): PostgreSQL's
/// own ICU collation, which initdb makes, on task_items.title, users.display_name, users.email,
/// projects.name and roles.name, with their indexes rebuilt on it. The tests that need an old database run
/// on one of their own in the same container, without a pool, as AddTaskAssigneesMigrationTests do.
/// </summary>
public sealed class AddRomanianCollationMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private const string Before = "20261107000923_AddTaskAssignees";
    private const string Collation = "ro-x-icu";

    private static readonly string[] _sortedColumns = ["projects.name", "roles.name", "task_items.title", "users.display_name", "users.email"];

    private static readonly Guid _role = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid _project = Guid.Parse("00000000-0000-7000-8000-000000000001");
    private static readonly Guid _column = Guid.Parse("00000000-0000-7000-8000-0000000000c1");

    // In the order of the Romanian alphabet. Byte by byte, each one starting with Ă, Â, Î, Ș or Ț came after Z.
    private static readonly string[] _titles = ["Acoperiș", "Ăla", "Âncă", "Inel", "Îmbinare", "Sanie", "Șanț", "Tablă", "Țiglă", "Zid"];

    [Fact]
    public async Task Migrate_PutsTheCollationOnTheSortedColumnsOnly()
    {
        await using var db = Postgres.CreateContext();

        (await CollatedColumns(db)).Should().Equal(_sortedColumns.Select(column => $"{column} {Collation}"));
    }

    [Fact]
    public async Task Migrate_RebuildsTheIndexesOnThoseColumnsWithIt()
    {
        await using var db = Postgres.CreateContext();

        (await Rows(db, """
            SELECT i.relname, c.collname
            FROM pg_index entry
            JOIN pg_class i ON i.oid = entry.indexrelid
            JOIN pg_collation c ON c.oid = entry.indcollation[0]
            WHERE i.relname IN ('ux_users_email', 'ux_roles_name', 'ux_roles_lower_name')
            ORDER BY i.relname
            """)).Select(row => $"{row[0]} {row[1]}").Should().Equal(
            $"ux_roles_lower_name {Collation}",
            $"ux_roles_name {Collation}",
            $"ux_users_email {Collation}");
    }

    [Fact]
    public async Task Migrate_OverOldData_KeepsEveryRowAndSortsItTheRomanianWay()
    {
        await using var db = await AnOldDatabase();
        var before = await Everything(db);
        (await Titles(db)).Should().Equal(["Acoperiș", "Inel", "Sanie", "Tablă", "Zid", "Âncă", "Îmbinare", "Ăla", "Șanț", "Țiglă"], "PostgreSQL compared them byte by byte");

        await db.GetService<IMigrator>().MigrateAsync();

        (await Everything(db)).Should().Equal(before, (actual, expected) => actual.SequenceEqual(expected));
        (await Titles(db)).Should().Equal(_titles);
        (await Rows(db, "SELECT display_name FROM users ORDER BY upper(display_name)")).Select(row => row[0])
            .Should().Equal("Ana Ene", "ăla Mic", "Îon Rus", "Sara Dinu", "ștefan Pop", "Țone Matei", "Zoe Ene");
        (await Rows(db, "SELECT email FROM users ORDER BY email")).Select(row => row[0])
            .Should().Equal("ana@example.com", "ăla@example.com", "îon@example.com", "sara@example.com", "ștefan@example.com", "țone@example.com", "zoe@example.com");
        (await Rows(db, "SELECT name FROM projects ORDER BY upper(name)")).Select(row => row[0])
            .Should().Equal("Acasă", "Șantier", "Zonă");
        (await Rows(db, "SELECT name FROM roles ORDER BY name")).Select(row => row[0])
            .Should().Equal("Membru", "Șef de echipă", "Zidar");
    }

    [Fact]
    public async Task Migrate_OverOldData_MakesTheRoleNamesUniqueInAnyCaseOfTheRomanianLettersToo()
    {
        await using var db = await AnOldDatabase();
        await db.GetService<IMigrator>().MigrateAsync();

        // The index is rebuilt with the collation, whose lower() lowers Ș: the local PostgreSQL's default, C, lowers A to Z only.
        var insert = () => Execute(db, "INSERT INTO roles (id, name, is_system, holders_version, created_at) VALUES (gen_random_uuid(), 'ȘEF DE ECHIPĂ', false, 0, now())");

        (await insert.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ux_roles_lower_name");
    }

    [Fact]
    public async Task Migrate_Back_LeavesTheColumnsWithTheDefaultCollationAndTheRows()
    {
        await using var db = await AnOldDatabase();
        var before = await Everything(db);
        await db.GetService<IMigrator>().MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(Before);

        (await CollatedColumns(db)).Should().BeEmpty();
        (await Everything(db)).Should().Equal(before, (actual, expected) => actual.SequenceEqual(expected));
    }

    /// <summary>A database migrated up to the one before, with names and titles starting with each of the Romanian letters.</summary>
    private async Task<AppDbContext> AnOldDatabase()
    {
        var database = $"migration_collation_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        await db.GetService<IMigrator>().MigrateAsync(Before);

        var users = new[] { ("Zoe Ene", "zoe"), ("Țone Matei", "țone"), ("ștefan Pop", "ștefan"), ("Sara Dinu", "sara"), ("Îon Rus", "îon"), ("ăla Mic", "ăla"), ("Ana Ene", "ana") }
            .Select(user => $"(gen_random_uuid(), '{user.Item2}@example.com', '{user.Item1}', 'x', '{_role}', true, 0, false, now())");
        var tasks = _titles.Reverse().Select((title, index) =>
            $"(gen_random_uuid(), '{_project}', '{_column}', {index + 1}, '{title}', 'Low', {index}, now())");
        await Execute(db, $"""
            INSERT INTO roles (id, name, is_system, holders_version, created_at) VALUES
                ('{_role}', 'Membru', false, 0, now()),
                (gen_random_uuid(), 'Zidar', false, 0, now()),
                (gen_random_uuid(), 'Șef de echipă', false, 0, now());
            INSERT INTO users (id, email, display_name, password_hash, role_id, is_active, failed_login_attempts, must_change_password, created_at) VALUES
                {string.Join(",\n", users)};
            INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES
                ('{_project}', 'Zonă', 'ZON', 11, now()),
                (gen_random_uuid(), 'Șantier', 'SAN', 1, now()),
                (gen_random_uuid(), 'Acasă', 'ACA', 1, now());
            INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES ('{_column}', '{_project}', 'De făcut', 0, false, now());
            INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at) VALUES
                {string.Join(",\n", tasks)};
            """);
        return db;
    }

    private static async Task<List<string>> CollatedColumns(AppDbContext db) =>
        [.. (await Rows(db, """
            SELECT table_name || '.' || column_name || ' ' || collation_name FROM information_schema.columns
            WHERE table_schema = 'public' AND collation_name IS NOT NULL
            ORDER BY table_name, column_name
            """)).Select(row => (string)row[0])];

    private static async Task<List<object>> Titles(AppDbContext db) =>
        [.. (await Rows(db, "SELECT title FROM task_items ORDER BY title")).Select(row => row[0])];

    /// <summary>Every row of the tables the migration changes, in an order that doesn't depend on the collation.</summary>
    private static async Task<List<object[]>> Everything(AppDbContext db) =>
        await Rows(db, """
            SELECT 'role', id, name, NULL FROM roles
            UNION ALL SELECT 'user', id, display_name, email FROM users
            UNION ALL SELECT 'project', id, name, key FROM projects
            UNION ALL SELECT 'task', id, title, number::text FROM task_items
            ORDER BY 1, 2
            """);

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
