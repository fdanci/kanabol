using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Builders;
using Kanabol.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Kanabol.Tests.Shared.Database;

/// <summary>
/// A real PostgreSQL, the same image compose uses, migrated once per test assembly. InMemory and
/// SQLite would behave differently on xmin, timestamptz and transactions (SPEC section 11.1).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>The image compose builds, from the same Dockerfile (SPEC section 19.1).</summary>
    public const string Image = "kanabol-postgres:17.11-pgvector0.8.7";

    // Built once per test assembly from database/Dockerfile. Docker's cache makes a build without changes
    // take a second, and building every time means a changed Dockerfile is never tested on a stale image
    // under the same name. A copy of backend/ alone, as some checks make, has no Dockerfile: it runs on
    // the image the checkout last built.
    private static readonly Lazy<Task> _imageBuilt = new(() => DatabaseFolder() is { } folder
        ? new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(folder)
            .WithDockerfile("Dockerfile")
            .WithName(Image)
            .WithCleanUp(false)
            .Build()
            .CreateAsync()
        : Task.CompletedTask);

    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;
    private Respawner? _respawner;

    public string ConnectionString => _container!.GetConnectionString();

    /// <summary>The repo's database/ folder, found upward from this source file, or null outside a checkout.</summary>
    private static string? DatabaseFolder([CallerFilePath] string sourceFile = "")
    {
        for (var folder = Path.GetDirectoryName(sourceFile); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            var database = Path.Combine(folder, "database");
            if (File.Exists(Path.Combine(database, "Dockerfile")))
            {
                return database;
            }
        }

        return null;
    }

    public async Task InitializeAsync()
    {
        await _imageBuilt.Value;
        _container = new PostgreSqlBuilder(Image).Build();
        await _container.StartAsync();

        var dataSource = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSource.UseVector();
        _dataSource = dataSource.Build();

        await using (var db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);
    }

    /// <summary>
    /// Empties the pool of the contexts below. They have their own data source, for pgvector's types, and
    /// NpgsqlConnection.ClearAllPools() doesn't reach a data source's pool, so a test that ends the
    /// database's connections clears it, or its next read gets one of the dead ones.
    /// </summary>
    public void ClearPool() => _dataSource!.Clear();

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A plain context, without the interceptors, for setting up and reading back data.</summary>
    public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_dataSource!, npgsql => npgsql.UseVector())
        .UseSnakeCaseNamingConvention()
        .Options);
}
