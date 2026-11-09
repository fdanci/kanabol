using System.Collections.Concurrent;
using System.Diagnostics;
using Kanabol.Api.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The hub's sends (Tasks.md group 19; SPEC section 9): HubBoardNotifier only queues them, in an unbounded
/// channel, so the request that made a change never waits for anyone's socket, and BoardSender, a hosted
/// service, is the one consumer: it sends them in order, logs a send that fails and goes on, and at stop
/// sends what's left for at most 5 seconds. The hub here is a stand-in that can hang or fail.
/// </summary>
public sealed class BoardSenderTests : IAsyncLifetime, IDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly BoardOutbox _queue = new();
    private readonly IHubContext<BoardHub> _hub = Substitute.For<IHubContext<BoardHub>>();
    private readonly ConcurrentQueue<string> _sent = new();
    private readonly RecordingLogger<BoardSender> _senderLog = new();
    private readonly HubBoardNotifier _notifier;
    private readonly BoardSender _sender;

    // Each send waits on the gate, open unless a test closes it.
    private TaskCompletionSource _gate = Open();

    public BoardSenderTests()
    {
        _hub.Clients.Group(Arg.Any<string>()).Returns(call =>
        {
            var group = call.Arg<string>();
            var groupProxy = Substitute.For<IClientProxy>();
            groupProxy.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(async send =>
            {
                var gate = _gate.Task;
                await gate.WaitAsync(send.Arg<CancellationToken>());
                var marker = send.Arg<object?[]>()[0]?.ToString();
                if (marker == "fails")
                {
                    throw new InvalidOperationException("The hub failed.");
                }

                _sent.Enqueue($"{group} {send.Arg<string>()} {marker}");
            });
            return groupProxy;
        });
        _notifier = new HubBoardNotifier(_queue, new RecordingLogger<HubBoardNotifier>());
        _sender = new BoardSender(_queue, _hub, _senderLog);
    }

    public Task InitializeAsync() => _sender.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        _gate.TrySetResult();
        await _sender.StopAsync(CancellationToken.None);
    }

    public void Dispose() => _sender.Dispose();

    private static TaskCompletionSource Open()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gate.SetResult();
        return gate;
    }

    private void CloseTheGate() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private async Task SentCount(int count)
    {
        var watch = Stopwatch.StartNew();
        while (_sent.Count < count && watch.Elapsed < _patience)
        {
            await Task.Delay(10);
        }

        _sent.Should().HaveCount(count);
    }

    [Fact]
    public async Task Notify_WhileTheHubHangs_ReturnsAtOnce()
    {
        CloseTheGate();

        var project = _notifier.NotifyProjectAsync(Guid.CreateVersion7(), "TaskCreated", "a", CancellationToken.None);
        var user = _notifier.NotifyUserAsync(Guid.CreateVersion7(), "NotificationCreated", "b", CancellationToken.None);

        project.IsCompletedSuccessfully.Should().BeTrue("the request only queues the message");
        user.IsCompletedSuccessfully.Should().BeTrue();
        await Task.Delay(100);
        _sent.Should().BeEmpty("the hub still hangs");
    }

    [Fact]
    public async Task Sender_SendsToTheRightGroups_InTheOrderTheyCame()
    {
        var project = Guid.CreateVersion7();
        var user = Guid.CreateVersion7();

        for (var i = 0; i < 200; i++)
        {
            await (i % 2 == 0
                ? _notifier.NotifyProjectAsync(project, "TaskMoved", i, CancellationToken.None)
                : _notifier.NotifyUserAsync(user, "NotificationCreated", i, CancellationToken.None));
        }

        await SentCount(200);
        _sent.Should().Equal(Enumerable.Range(0, 200).Select(i => i % 2 == 0
            ? $"{BoardGroups.Project(project)} TaskMoved {i}"
            : $"{BoardGroups.User(user)} NotificationCreated {i}"));
    }

    [Fact]
    public async Task Sender_WhileTheHubHangs_KeepsTheRestInOrder_AndSendsThemOnceItFrees()
    {
        var project = Guid.CreateVersion7();
        CloseTheGate();

        foreach (var marker in new[] { "first", "second", "third" })
        {
            await _notifier.NotifyProjectAsync(project, "TaskUpdated", marker, CancellationToken.None);
        }

        await Task.Delay(100);
        _sent.Should().BeEmpty();
        _gate.SetResult();

        await SentCount(3);
        _sent.Select(sent => sent.Split(' ')[2]).Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task Sender_WhenASendFails_LogsItAndSendsTheNext()
    {
        var project = Guid.CreateVersion7();

        await _notifier.NotifyProjectAsync(project, "TaskCreated", "fails", CancellationToken.None);
        await _notifier.NotifyProjectAsync(project, "TaskCreated", "after", CancellationToken.None);

        await SentCount(1);
        _sent.Should().ContainSingle().Which.Should().EndWith("after");
        _senderLog.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Message.Contains("TaskCreated", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stop_SendsWhatIsLeft_BeforeItReturns()
    {
        var project = Guid.CreateVersion7();
        CloseTheGate();
        foreach (var marker in new[] { "one", "two", "three" })
        {
            await _notifier.NotifyProjectAsync(project, "TaskArchived", marker, CancellationToken.None);
        }

        var stopping = _sender.StopAsync(CancellationToken.None);
        await Task.Delay(200);
        stopping.IsCompleted.Should().BeFalse("three messages wait to go");
        _gate.SetResult();
        await stopping.WaitAsync(_patience);

        _sent.Select(sent => sent.Split(' ')[2]).Should().Equal("one", "two", "three");
    }

    // The spec's 5 seconds, measured: a hub that never answers doesn't hold the API's shutdown longer.
    [Fact]
    public async Task Stop_WhileTheHubHangs_GivesUpAfterFiveSeconds_AndSaysHowManyWereLeft()
    {
        var project = Guid.CreateVersion7();
        CloseTheGate();
        foreach (var marker in new[] { "one", "two" })
        {
            await _notifier.NotifyProjectAsync(project, "TaskArchived", marker, CancellationToken.None);
        }

        await Task.Delay(100);
        var watch = Stopwatch.StartNew();
        await _sender.StopAsync(CancellationToken.None).WaitAsync(_patience);
        watch.Stop();

        watch.Elapsed.Should().BeGreaterThan(TimeSpan.FromSeconds(4.5)).And.BeLessThan(TimeSpan.FromSeconds(7));
        _sent.Should().BeEmpty();
        _senderLog.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("2 board messages", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Notify_AfterStop_IsDropped()
    {
        await _sender.StopAsync(CancellationToken.None);

        var late = _notifier.NotifyProjectAsync(Guid.CreateVersion7(), "TaskCreated", "late", CancellationToken.None);

        late.IsCompletedSuccessfully.Should().BeTrue("a change saved while the API stops is still saved");
        await Task.Delay(100);
        _sent.Should().BeEmpty();
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
