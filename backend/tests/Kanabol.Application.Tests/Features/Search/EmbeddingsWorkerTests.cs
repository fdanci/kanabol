using System.Collections.Concurrent;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Search;
using Kanabol.Application.Features.Search.Commands.EmbedTasks;
using Kanabol.Application.Tests.Support;
using Kanabol.Infrastructure.Embeddings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kanabol.Application.Tests.Features.Search;

/// <summary>
/// SPEC section 19.5: EmbeddingsWorker runs EmbedTasksCommand at start and then every Embeddings:Interval, in
/// batches of Embeddings:BatchSize, each time in a scope of its own; a run that fails is logged and the next one
/// still comes. Embeddings:Enabled false stops it. Stopping the API in the middle of a run cancels it without
/// counting as a failure. The interval here is shorter than the minute the options allow, which only the options'
/// validation refuses.
/// </summary>
public sealed class EmbeddingsWorkerTests : IAsyncDisposable
{
    private readonly ICommandHandler<EmbedTasksCommand, EmbeddedTasks> _handler = Substitute.For<ICommandHandler<EmbedTasksCommand, EmbeddedTasks>>();
    private readonly ConcurrentQueue<EmbedTasksCommand> _commands = new();
    private readonly ListLoggerProvider _logs = new();
    private readonly ServiceProvider _services;
    private int _scopes;

    public EmbeddingsWorkerTests()
    {
        Answer(_ => Task.FromResult<Result<EmbeddedTasks>>(new EmbeddedTasks(3, 1, 42, 0)));

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

    private int Runs => _commands.Count;

    // Each run is recorded before the handler answers, so a test can wait for it.
    private void Answer(Func<CancellationToken, Task<Result<EmbeddedTasks>>> answer) =>
        _handler.Handle(Arg.Any<EmbedTasksCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _commands.Enqueue(call.Arg<EmbedTasksCommand>());
                return answer(call.Arg<CancellationToken>());
            });

    private EmbeddingsWorker Worker(bool enabled = true, int intervalMs = 50, int batchSize = 64) =>
        new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmbeddingsOptions
            {
                Enabled = enabled,
                Interval = TimeSpan.FromMilliseconds(intervalMs),
                BatchSize = batchSize,
                Model = "text-embedding-3-small",
            }),
            _services.GetRequiredService<ILogger<EmbeddingsWorker>>());

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
    public async Task Start_RunsTheVectorizationAtOnce_InBatchesOfTheConfiguredSize()
    {
        using var worker = Worker(intervalMs: 60_000, batchSize: 16);

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs == 1);
        await worker.StopAsync(CancellationToken.None);

        _commands.Should().ContainSingle("the next run is a minute away").Which.BatchSize.Should().Be(16);
        _logs.Entries.Should().Contain(entry => entry.Message == "The vectorization runs now and every 00:01:00, in batches of 16");
    }

    [Fact]
    public async Task Start_ThenRunsItEveryInterval_EachInAScopeOfItsOwn()
    {
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs >= 3);
        await worker.StopAsync(CancellationToken.None);

        _scopes.Should().Be(Runs);
    }

    [Fact]
    public async Task Run_ThatThrows_IsLogged_AndTheNextOneStillComes()
    {
        var calls = 0;
        Answer(_ => Interlocked.Increment(ref calls) == 1
            ? throw new InvalidOperationException("The database is gone")
            : Task.FromResult<Result<EmbeddedTasks>>(new EmbeddedTasks(1, 1, 7, 0)));
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs >= 2);
        await worker.StopAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The vectorization failed");
        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public async Task Run_WhenTheModelFails_IsLoggedWithItsCode_AndTheNextOneStillComes()
    {
        Answer(_ => Task.FromResult<Result<EmbeddedTasks>>(EmbeddingErrors.ModelFailed));
        using var worker = Worker();

        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs >= 2);
        await worker.StopAsync(CancellationToken.None);

        _logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message == "The vectorization failed with Embeddings.ModelFailed");
    }

    [Fact]
    public async Task Start_WhenTheVectorizationIsOff_RunsNothing()
    {
        using var worker = Worker(enabled: false);

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await worker.StopAsync(CancellationToken.None);

        Runs.Should().Be(0);
        _logs.Entries.Should().Contain(entry => entry.Message == "The vectorization is turned off");
    }

    [Fact]
    public async Task Stop_WhileItWaits_EndsTheWorker()
    {
        using var worker = Worker(intervalMs: 60_000);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs == 1);

        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    // A first run over many tasks can take minutes: a stop then cancels it, and the batches saved before stay.
    [Fact]
    public async Task Stop_DuringARun_CancelsIt_AndLogsNoFailure()
    {
        Answer(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new EmbeddedTasks(0, 0, 0, 0);
        });
        using var worker = Worker(intervalMs: 60_000);
        await worker.StartAsync(CancellationToken.None);
        await Until(() => Runs == 1);

        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
        _logs.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
    }
}
