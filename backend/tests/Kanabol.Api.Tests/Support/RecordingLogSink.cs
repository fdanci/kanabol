using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace Kanabol.Api.Tests.Support;

/// <summary>Keeps what the API logs through its real pipeline, with the properties the log context adds.</summary>
public sealed class RecordingLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}
