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
        ? Task.Run(() => BuildImageAlone(folder))
        : Task.CompletedTask);

    /// <summary>
    /// The two test assemblies run at once and build the same tag. Testcontainers deletes an image of the same name
    /// before it builds, unless told not to, so one assembly's build took away the image the other had just built
    /// and was starting a container from ("has not been created" in CI on e4849af, "No such image" here). The image
    /// is never deleted now, and a machine-wide mutex keeps the builds one at a time, the second from Docker's cache.
    /// </summary>
    private static void BuildImageAlone(string folder)
    {
        using var mutex = new Mutex(initiallyOwned: false, @"Global\kanabol-postgres-image");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromMinutes(10)))
            {
                throw new TimeoutException($"Another test run has been building {Image} for 10 minutes.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The run that held it ended without letting go: this one has it now.
        }

        // A mutex belongs to the thread that took it, so the build waits here, on that thread.
        try
        {
            new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(folder)
                .WithDockerfile("Dockerfile")
                .WithName(Image)
                .WithDeleteIfExists(false)
                .WithCleanUp(false)
                .Build()
                .CreateAsync()
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

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
