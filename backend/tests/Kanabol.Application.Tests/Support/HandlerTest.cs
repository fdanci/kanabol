using Kanabol.Application.Abstractions;
using Kanabol.Infrastructure;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Tests.Support;

/// <summary>
/// Handlers as the API gets them, with their decorators and the real AppDbContext. What can't
/// come from a database is substituted: the user, the request, the clock and the board notifications.
/// </summary>
public abstract class HandlerTest(PostgresFixture postgres) : PostgresTest(postgres)
{
    private ServiceProvider? _services;
    private AsyncServiceScope _scope;

    protected ICurrentUser CurrentUser { get; } = Substitute.For<ICurrentUser>();

    protected IClock Clock { get; } = Substitute.For<IClock>();

    protected IBoardNotifier BoardNotifier { get; } = Substitute.For<IBoardNotifier>();

    /// <summary>The request's X-Connection-Id, none unless a test says so.</summary>
    protected IRequestContext RequestContext { get; } = Substitute.For<IRequestContext>();

    protected ListLoggerProvider Logs { get; } = new();

    /// <summary>Where LocalFileStorage keeps this test's files: a folder of its own, which goes with the test.</summary>
    protected string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), "kanabol-tests", "handler-attachments", Guid.NewGuid().ToString("N"));

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = Postgres.ConnectionString,
                ["Jwt:Secret"] = "only-for-these-tests-and-long-enough-to-pass",
                ["Jwt:Issuer"] = "kanabol-tests",
                ["Jwt:Audience"] = "kanabol-tests",
                ["Storage:Root"] = StorageRoot,
                ["Storage:AllowedExtensions"] = "pdf, .PNG,txt",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddHandlersFrom(typeof(HandlerTest).Assembly);
        services.AddSingleton(CurrentUser);
        services.AddSingleton(Clock);
        services.AddSingleton(BoardNotifier);
        services.AddSingleton(RequestContext);
        ConfigureServices(services);

        _services = services.BuildServiceProvider(validateScopes: true);
        _scope = _services.CreateAsyncScope();
    }

    public override async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await (_services?.DisposeAsync() ?? ValueTask.CompletedTask);
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }

        await base.DisposeAsync();
    }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected TService Get<TService>()
        where TService : notnull =>
        _scope.ServiceProvider.GetRequiredService<TService>();

    /// <summary>A scope of its own, for what has to run side by side, as two requests would.</summary>
    protected AsyncServiceScope NewScope() => _services!.CreateAsyncScope();
}
