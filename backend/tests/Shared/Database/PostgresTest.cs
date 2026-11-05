namespace Kanabol.Tests.Shared.Database;

/// <summary>Starts each test from empty tables.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public abstract class PostgresTest(PostgresFixture postgres) : IAsyncLifetime
{
    protected PostgresFixture Postgres { get; } = postgres;

    public virtual Task InitializeAsync() => Postgres.ResetAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;
}
