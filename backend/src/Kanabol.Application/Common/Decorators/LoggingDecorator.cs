using System.Diagnostics;
using Kanabol.Application.Abstractions.Messaging;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Common.Decorators;

/// <summary>Logs each command's name, outcome and duration, but never its contents.</summary>
public sealed class LoggingDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingDecorator<TCommand, TResult>> logger) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    private static readonly string _commandName = typeof(TCommand).Name;

    public async Task<Result<TResult>> Handle(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.Handle(command, ct);
        var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        if (result.IsSuccess)
        {
            logger.LogInformation("Command {Command} succeeded in {ElapsedMs:0} ms", _commandName, elapsedMs);
        }
        else
        {
            var errorCode = result.Error.Code;
            logger.LogWarning(
                "Command {Command} failed with {ErrorCode} in {ElapsedMs:0} ms",
                _commandName,
                errorCode,
                elapsedMs);
        }

        return result;
    }
}
