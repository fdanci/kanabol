using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;
using Kanabol.Application.Tests.Support;
using Kanabol.Infrastructure.Alerts;
using Kanabol.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Notifications;

/// <summary>
/// SPEC section 4.3, "Background service": OverdueTasksWorker runs NotifyOverdueTasksCommand at start and then
/// every Alerts:OverdueCheckInterval, each time in a scope of its own; a run that fails is logged and the next
/// one still comes. Alerts:OverdueCheckEnabled false stops it. The interval here is shorter than the minute
/// the options allow, which only the options' validation refuses.
/// </summary>
public sealed class OverdueTasksWorkerTests : IAsyncDisposable
{
    private readonly ICommandHandler<NotifyOverdueTasksCommand, int> _handler = Substitute.For<ICommandHandler<NotifyOverdueTasksCommand, int>>();
    private readonly ListLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private int _scopes;
    private int _runs;

    public OverdueTasksWorkerTests()
    {
        _handler.Handle(Arg.Any<NotifyOverdueTasksCommand>(), Arg.Any<CancellationToken>())
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

    private OverdueTasksWorker Worker(bool enabled = true, int intervalMs = 50) =>
        new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new AlertsOptions { OverdueCheckEnabled = enabled, OverdueCheckInterval = TimeSpan.FromMilliseconds(intervalMs) }),
            _services.GetRequiredService<ILogger<OverdueTasksWorker>>());

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
    public async Task Start_RunsTheCheckAtOnce()
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
        _handler.Handle(Arg.Any<NotifyOverdueTasksCommand>(), Arg.Any<CancellationToken>())
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

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The overdue check failed");
        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Run_ThatFails_IsLoggedWithItsCode()
    {
        _handler.Handle(Arg.Any<NotifyOverdueTasksCommand>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref _runs);
                return Task.FromResult<Result<int>>(Error.Failure("Database.Unavailable", "Gone"));
            });
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Volatile.Read(ref _runs) >= 2);
        await worker.StopAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The overdue check failed with Database.Unavailable");
    }

    [Fact]
    public async Task Start_WhenTurnedOff_RunsNothing()
    {
        using var worker = Worker(enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        _runs.Should().Be(0);
        _logs.Entries.Should().Contain(entry => entry.Message == "The overdue check is turned off");
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
