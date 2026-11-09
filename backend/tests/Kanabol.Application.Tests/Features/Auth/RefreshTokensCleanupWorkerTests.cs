using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth.Commands.DeleteOldRefreshTokens;
using Kanabol.Application.Tests.Support;
using Kanabol.Infrastructure.Authentication;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Auth;

/// <summary>
/// SPEC section 4.5: RefreshTokensCleanupWorker runs DeleteOldRefreshTokensCommand at start and then every
/// Auth:RefreshTokenCleanupInterval, each time in a scope of its own, as OverdueTasksWorker does; a run that
/// fails is logged and the next one still comes. Auth:RefreshTokenCleanupEnabled false stops it. The interval
/// here is shorter than the hour the options allow, which only the options' validation refuses.
/// </summary>
public sealed class RefreshTokensCleanupWorkerTests : IAsyncDisposable
{
    private readonly ICommandHandler<DeleteOldRefreshTokensCommand, int> _handler = Substitute.For<ICommandHandler<DeleteOldRefreshTokensCommand, int>>();
    private readonly ListLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private int _scopes;
    private int _runs;

    public RefreshTokensCleanupWorkerTests()
    {
        _handler.Handle(Arg.Any<DeleteOldRefreshTokensCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref _runs);
                return Task.FromResult<Result<int>>(0);
            });

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
        services.AddScoped(_ =>
        {
            Interlocked.Increment(ref _scopes);
            return _handler;
        });
        _services = services.BuildServiceProvider(validateScopes: true);
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    private RefreshTokensCleanupWorker Worker(bool enabled = true, int intervalMs = 50) =>
        new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AuthOptions { RefreshTokenCleanupEnabled = enabled, RefreshTokenCleanupInterval = TimeSpan.FromMilliseconds(intervalMs) }),
            _services.GetRequiredService<ILogger<RefreshTokensCleanupWorker>>());

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the worker should have got there by now");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Start_RunsTheCleanupAtOnce()
    {
        using var worker = Worker(intervalMs: 60_000);

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) == 1);
        await worker.StopAsync(CancellationToken.None);

        _runs.Should().Be(1, "the next run is a minute away");
    }

    [Fact]
    public async Task Start_ThenRunsItEveryIntervalEachInAScopeOfItsOwn()
    {
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) >= 3);
        await worker.StopAsync(CancellationToken.None);

        _scopes.Should().Be(_runs);
    }

    [Fact]
    public async Task Run_ThatThrows_IsLoggedAndTheNextOneStillComes()
    {
        _handler.Handle(Arg.Any<DeleteOldRefreshTokensCommand>(), Arg.Any<CancellationToken>())
            .Returns(
                _ =>
                {
                    Interlocked.Increment(ref _runs);
                    throw new InvalidOperationException("The database is gone");
                },
                _ =>
                {
                    Interlocked.Increment(ref _runs);
                    return Task.FromResult<Result<int>>(2);
                });
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) >= 2);
        await worker.StopAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The refresh tokens' cleanup failed");
        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Run_ThatFails_IsLoggedWithItsCode()
    {
        _handler.Handle(Arg.Any<DeleteOldRefreshTokensCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref _runs);
                return Task.FromResult<Result<int>>(Error.Failure("Database.Unavailable", "Gone"));
            });
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) >= 2);
        await worker.StopAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The refresh tokens' cleanup failed with Database.Unavailable");
    }

    [Fact]
    public async Task Start_WhenTurnedOff_RunsNothing()
    {
        using var worker = Worker(enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        _runs.Should().Be(0);
        _logs.Entries.Should().Contain(entry => entry.Message == "The refresh tokens' cleanup is turned off");
    }

    [Fact]
    public async Task Stop_WhileItWaits_EndsTheWorker()
    {
        using var worker = Worker(intervalMs: 60_000);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) == 1);

        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompleted.Should().BeTrue();
        worker.ExecuteTask.IsFaulted.Should().BeFalse();
    }
}
