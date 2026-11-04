using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Application.Tests.Support;

/// <summary>
/// Handlers as the API gets them, with their decorators and the real AppDbContext. What can't
/// come from a database is substituted: the user, the clock and the board notifications.
/// </summary>
public abstract class HandlerTest(PostgresFixture postgres) : PostgresTest(postgres)
{
    private ServiceProvider? _services;
    private AsyncServiceScope _scope;

    protected ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    protected IClock Clock { get; } = Substitute.For<IClock>();

    protected IBoardNotifier BoardNotifier { get; } = Substitute.For<IBoardNotifier>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Postgres.ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddHandlersFrom(typeof(HandlerTest).Assembly);
        services.AddSingleton(CurrentUser);
        services.AddSingleton(Clock);
        services.AddSingleton(BoardNotifier);

        _services = services.BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
    }

    public override async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await (_services?.DisposeAsync() ?? ValueTask.CompletedTask);
        await base.DisposeAsync();
    }

    protected TService Get<TService>()
        where TService : notnull =>
        _scope.ServiceProvider.GetRequiredService<TService>();
}
