using Kanabol.Infrastructure.Persistence;
using Kanabol.Tests.Shared.Database;
using Kanabol.Tests.Shared.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Support;

/// <summary>One container and one running API for the whole assembly, because starting them is slow.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly Lazy<ApiFactory> _searching;

    public ApiFixture()
    {
        _searching = new(() =>
        {
            var searching = new ApiFactory(Postgres.ConnectionString, () => EmbeddingModel);
            _ = searching.Services;
            return searching;
        });
    }

    public PostgresFixture Postgres { get; } = new();

    public ApiFactory Factory { get; private set; } = null!;

    /// <summary>The model the search's API answers with: each of the search's tests gives it a new one.</summary>
    public FakeEmbeddingModel EmbeddingModel { get; set; } = new();

    /// <summary>
    /// A second API on the same database, for the search (SPEC section 19.6): the vectorization on, with
    /// EmbeddingModel, and no worker. Started on first use, since only the search's tests need it; the tests of
    /// one collection run one after another, so the two APIs never serve a test at the same time.
    /// </summary>
    public ApiFactory Searching => _searching.Value;

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
        if (_searching.IsValueCreated)
        {
            await _searching.Value.DisposeAsync();
        }

        await Factory.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}
