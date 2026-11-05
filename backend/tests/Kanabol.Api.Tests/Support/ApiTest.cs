namespace Kanabol.Api.Tests.Support;

/// <summary>Starts each test from a freshly seeded database.</summary>
[Collection(ApiCollectionDefinition.Name)]
public abstract class ApiTest(ApiFixture api) : IAsyncLifetime
{
    protected ApiFixture Api { get; } = api;

    public virtual Task InitializeAsync() => Api.ResetAsync();

    public virtual Task DisposeAsync() => Task.CompletedTask;
}
