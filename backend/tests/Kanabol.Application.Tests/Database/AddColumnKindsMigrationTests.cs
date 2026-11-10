using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// The four fixed columns over the projects a database already has (SPEC section 4.4, "Coloanele fixe",
/// group 22): the first column becomes Backlog, the first done column after it Gata, last, a column named
/// "În lucru" in any case that column, or a new one after Backlog, and a new "În pauză" after it. No task
/// changes its column; the ones finished outside Gata are open again. The old database is one of its own
/// in the same container, without a pool, as in AddRomanianCollationMigrationTests.
/// </summary>
public sealed class AddColumnKindsMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    private const string Before = "20261109202207_AddRomanianCollation";

    // An ordinary project, as the API made them until now.
    private const string Usual = "00000000-0000-7000-8000-000000000001";

    // Two done columns: Gata, then Livrat, whose tasks are open again.
    private const string TwoDone = "00000000-0000-7000-8000-000000000002";

    // One column, which keeps its tasks and becomes Backlog.
    private const string Single = "00000000-0000-7000-8000-000000000003";

    // One column, done, which is also the first: it becomes Backlog, and a new Gata comes.
    private const string SingleDone = "00000000-0000-7000-8000-000000000004";

    // The project's own columns, with "ÎN LUCRU" among them in capitals.
    private const string Own = "00000000-0000-7000-8000-000000000005";

    // Its own columns and no "În lucru": a new one comes right after Backlog.
    private const string OwnWithoutProgress = "00000000-0000-7000-8000-000000000006";

    private static readonly DateTimeOffset _finished = new(2026, 10, 20, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Migrate_AnOrdinaryProject_KeepsItsColumnsAndAddsÎnPauzăAfterÎnLucru()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Board(db, Usual)).Should().Equal(
            "0 Backlog Backlog c11",
            "1 În lucru InProgress c12",
            "2 În pauză Paused new",
            "3 Gata Done c13");
    }

    [Fact]
    public async Task Migrate_TwoDoneColumns_MakesTheFirstGataAndLastAndTheOtherOneOfTheProjectsOwn()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Board(db, TwoDone)).Should().Equal(
            "0 Backlog Backlog c21",
            "1 În lucru InProgress c22",
            "2 În pauză Paused new",
            "3 Livrat Custom c24",
            "4 Gata Done c23");
    }

    [Fact]
    public async Task Migrate_ASingleColumn_MakesItBacklogAndAddsTheOtherThree()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Board(db, Single)).Should().Equal(
            "0 Backlog Backlog c31",
            "1 În lucru InProgress new",
            "2 În pauză Paused new",
            "3 Gata Done new");
        (await Board(db, SingleDone)).Should().Equal(
            "0 Backlog Backlog c41",
            "1 În lucru InProgress new",
            "2 În pauză Paused new",
            "3 Gata Done new");
    }

    [Fact]
    public async Task Migrate_TheProjectsOwnColumns_KeepTheirNamesAndPlacesBetweenBacklogAndGata()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Board(db, Own)).Should().Equal(
            "0 Backlog Backlog c51",
            "1 Analiză Custom c52",
            "2 În lucru InProgress c53",
            "3 În pauză Paused new",
            "4 Review Custom c54",
            "5 Gata Done c55");
        (await Board(db, OwnWithoutProgress)).Should().Equal(
            "0 Backlog Backlog c61",
            "1 În lucru InProgress new",
            "2 În pauză Paused new",
            "3 Review Custom c62",
            "4 Gata Done c63");
    }

    [Fact]
    public async Task Migrate_Always_LeavesEveryTaskInItsColumnAndOpensThoseFinishedOutsideGata()
    {
        await using var db = await AnOldDatabase();
        var before = await Tasks(db);

        await db.GetService<IMigrator>().MigrateAsync();

        var after = await Tasks(db);
        after.Select(task => task.Column).Should().Equal(before.Select(task => task.Column));
        after.Select(task => task.Position).Should().Equal(before.Select(task => task.Position));
        after.Where(task => task.Completed).Select(task => task.Title).Should().BeEquivalentTo(
            "finished in Gata",
            "finished in Gata, two done",
            "finished in Livrat, archived",
            "finished in Livrat, own");
        after.Single(task => task.Title == "finished in Livrat, archived").CompletedAt.Should().Be(_finished, "an archived task keeps its moment");
    }

    [Fact]
    public async Task Migrate_Always_GivesEveryProjectEachFixedKindOnceAndTheNewColumnsVersion7Ids()
    {
        await using var db = await AnOldDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await Rows(db, "SELECT kind, count(*), count(DISTINCT project_id) FROM board_columns WHERE kind <> 'Custom' GROUP BY kind ORDER BY kind"))
            .Select(row => $"{row[0]} {row[1]} {row[2]}").Should().Equal("Backlog 6 6", "Done 6 6", "InProgress 6 6", "Paused 6 6");
        (await Rows(db, "SELECT count(*) FROM board_columns WHERE id::text NOT LIKE '00000000-%' AND substr(id::text, 15, 1) <> '7'"))
            .Single()[0].Should().Be(0L);
        var duplicate = () => Execute(db, $"INSERT INTO board_columns (id, project_id, name, position, kind, created_at) VALUES (gen_random_uuid(), '{Usual}', 'Altă Gata', 9, 'Done', now())");
        (await duplicate.Should().ThrowAsync<PostgresException>()).Which.ConstraintName.Should().Be("ux_board_columns_project_id_kind");
        await Execute(db, $"INSERT INTO board_columns (id, project_id, name, position, kind, created_at) VALUES (gen_random_uuid(), '{Usual}', 'Încă una', 9, 'Custom', now())");
    }

    [Fact]
    public async Task Migrate_Back_PutsIsDoneOnGataAndLeavesTheColumnsAsTheyAre()
    {
        await using var db = await AnOldDatabase();
        await db.GetService<IMigrator>().MigrateAsync();
        var columns = await Rows(db, "SELECT id, name, position FROM board_columns ORDER BY id");

        await db.GetService<IMigrator>().MigrateAsync(Before);

        (await Rows(db, "SELECT id, name, position FROM board_columns ORDER BY id"))
            .Should().Equal(columns, (actual, expected) => actual.SequenceEqual(expected));
        (await Rows(db, "SELECT name, count(*) FROM board_columns WHERE is_done GROUP BY name"))
            .Select(row => $"{row[0]} {row[1]}").Should().Equal("Gata 6");
        (await Rows(db, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'board_columns' AND column_name = 'kind'"))
            .Single()[0].Should().Be(0L);
    }

    /// <summary>The project's columns from the left: place, name, kind, and which old column it was, or new.</summary>
    private static async Task<List<string>> Board(AppDbContext db, string project) =>
        [.. (await Rows(db, $"SELECT position, name, kind, id::text FROM board_columns WHERE project_id = '{project}' ORDER BY position"))
            .Select(row => $"{row[0]} {row[1]} {row[2]} {Old((string)row[3])}")];

    private static string Old(string id) => id.StartsWith("00000000-0000-7000-8000-000000000c", StringComparison.Ordinal) ? $"c{id[^2..]}" : "new";

    private static async Task<List<(string Title, Guid Column, int Position, bool Completed, DateTimeOffset? CompletedAt)>> Tasks(AppDbContext db) =>
        [.. (await Rows(db, "SELECT title, column_id, position, completed_at FROM task_items ORDER BY number"))
            .Select(row => ((string)row[0], (Guid)row[1], (int)row[2], row[3] is not DBNull, row[3] is DateTime moment ? new DateTimeOffset(moment, TimeSpan.Zero) : (DateTimeOffset?)null))];

    /// <summary>A database migrated up to the one before, with the six projects above, their columns and tasks.</summary>
    private async Task<AppDbContext> AnOldDatabase()
    {
        var database = $"migration_kinds_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);
        await db.GetService<IMigrator>().MigrateAsync(Before);

        // Column ids end in their project's digit and their place from 1, c11 to c63.
        string Column(string project, int place) => $"00000000-0000-7000-8000-000000000c{project[^1]}{place}";
        string Columns(string project, params (string Name, bool Done)[] columns) =>
            string.Join(",\n", columns.Select((column, place) =>
                $"('{Column(project, place + 1)}', '{project}', '{column.Name}', {place}, {(column.Done ? "true" : "false")}, now())"));
        var number = 0;
        string Task(string project, int place, string title, bool finished = false, bool archived = false) =>
            $"(gen_random_uuid(), '{project}', '{Column(project, place)}', {++number}, '{title}', 'Low', {number}, now(), " +
            $"{(finished ? $"'{_finished:O}'" : "NULL")}, {(archived ? "now()" : "NULL")})";

        await Execute(db, $"""
            INSERT INTO projects (id, name, key, next_task_number, created_at) VALUES
                ('{Usual}', 'Obișnuit', 'OBI', 20, now()),
                ('{TwoDone}', 'Două gata', 'DOI', 20, now()),
                ('{Single}', 'Una', 'UNA', 20, now()),
                ('{SingleDone}', 'Una gata', 'UNG', 20, now()),
                ('{Own}', 'Ale lor', 'OWN', 20, now()),
                ('{OwnWithoutProgress}', 'Fără lucru', 'FAR', 20, now());
            INSERT INTO board_columns (id, project_id, name, position, is_done, created_at) VALUES
                {Columns(Usual, ("De făcut", false), ("În lucru", false), ("Gata", true))},
                {Columns(TwoDone, ("De făcut", false), ("În lucru", false), ("Gata", true), ("Livrat", true))},
                {Columns(Single, ("Tot", false))},
                {Columns(SingleDone, ("Gata", true))},
                {Columns(Own, ("Idei", false), ("Analiză", false), ("ÎN LUCRU", false), ("Review", false), ("Livrat", true))},
                {Columns(OwnWithoutProgress, ("Idei", false), ("Review", false), ("Livrat", true))};
            INSERT INTO task_items (id, project_id, column_id, number, title, priority, position, created_at, completed_at, archived_at) VALUES
                {Task(Usual, 1, "to do")},
                {Task(Usual, 2, "in progress")},
                {Task(Usual, 3, "finished in Gata", finished: true)},
                {Task(TwoDone, 3, "finished in Gata, two done", finished: true)},
                {Task(TwoDone, 4, "finished in Livrat", finished: true)},
                {Task(TwoDone, 4, "finished in Livrat, archived", finished: true, archived: true)},
                {Task(Single, 1, "the only column")},
                {Task(SingleDone, 1, "finished in the only column", finished: true)},
                {Task(Own, 3, "in capitals")},
                {Task(Own, 5, "finished in Livrat, own", finished: true)},
                {Task(OwnWithoutProgress, 2, "in review")};
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
