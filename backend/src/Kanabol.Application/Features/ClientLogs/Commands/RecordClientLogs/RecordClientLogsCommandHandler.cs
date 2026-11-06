using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.ClientLogs.Commands.RecordClientLogs;

/// <summary>
/// Writes the browser's warnings and errors into the API's own logs (SPEC section 8.3). The log context
/// adds the user and the correlation id of the request that brought them, as for any other line.
/// </summary>
public sealed class RecordClientLogsCommandHandler(ILogger<RecordClientLogsCommandHandler> logger)
    : ICommandHandler<RecordClientLogsCommand, Unit>
{
    public const int MaximumUserAgentLength = 512;

    public Task<Result<Unit>> Handle(RecordClientLogsCommand command, CancellationToken ct)
    {
        var userAgent = command.UserAgent is { Length: > MaximumUserAgentLength } longer
            ? longer[..MaximumUserAgentLength]
            : command.UserAgent;

        foreach (var entry in command.Entries)
        {
            var properties = new Dictionary<string, object?>
            {
                ["RequestCorrelationId"] = entry.CorrelationId,
                ["ClientDetails"] = entry.Details,
                ["UserAgent"] = userAgent,
            };
            using (logger.BeginScope(properties))
            {
                logger.Log(
                    entry.Level == ClientLogLevels.Error ? LogLevel.Error : LogLevel.Warning,
                    "Browser {ClientLevel} on {ClientUrl}: {ClientMessage}",
                    entry.Level,
                    entry.Url,
                    entry.Message);
            }
        }

        if (command.Dropped > 0)
        {
            logger.LogWarning("The browser dropped {DroppedCount} log entries over its limit", command.Dropped);
        }

        return Task.FromResult<Result<Unit>>(Unit.Value);
    }
}
