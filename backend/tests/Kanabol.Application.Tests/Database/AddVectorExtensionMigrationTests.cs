using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Kanabol.Application.Tests.Database;

/// <summary>
/// pgvector's extension (SPEC section 19.1, group 25): the migration creates it, so the vector type and the
/// cosine distance work, and going back drops it. On a database of its own, as the other migration tests,
/// since an extension belongs to one database.
/// </summary>
public sealed class AddVectorExtensionMigrationTests(PostgresFixture postgres) : PostgresTest(postgres)
{
    [Fact]
    public async Task Migrate_CreatesTheVectorExtension()
    {
        await using var db = await ANewDatabase();

        await db.GetService<IMigrator>().MigrateAsync();

        (await VectorExtensions(db)).Should().Be(1);
        var distance = await db.Database.SqlQueryRaw<double>("SELECT '[1,0]'::vector <=> '[0,1]'::vector AS \"Value\"").SingleAsync();
        distance.Should().Be(1, "two vectors at a right angle are at a cosine distance of 1");
    }

    [Fact]
    public async Task Migrate_Back_DropsTheVectorExtension()
    {
        await using var db = await ANewDatabase();
        await db.GetService<IMigrator>().MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync("20261110231829_AddWorkedTime");

        (await VectorExtensions(db)).Should().Be(0);
    }

    private static Task<long> VectorExtensions(AppDbContext db) =>
        db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM pg_extension WHERE extname = 'vector'").SingleAsync();

    private async Task<AppDbContext> ANewDatabase()
    {
        var database = $"migration_vector_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(Postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(Postgres.ConnectionString) { Database = database, Pooling = false }.ConnectionString;
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection, npgsql => npgsql.UseVector()).UseSnakeCaseNamingConvention().Options);
    }
}
