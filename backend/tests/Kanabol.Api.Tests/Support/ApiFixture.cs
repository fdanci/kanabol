using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Support;

/// <summary>One container and one running API for the whole assembly, because starting them is slow.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        Factory = new ApiFactory(Postgres.ConnectionString);
        _ = Factory.Services;
    }

    /// <summary>Empties the tables and seeds them again, as a new deployment would.</summary>
    public async Task ResetAsync()
    {
        await Postgres.ResetAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().Seed(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}
