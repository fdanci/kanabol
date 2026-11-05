namespace Kanabol.Tests.Shared.Database;

/// <summary>Tests in this collection share one container and don't run in parallel with each other.</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
