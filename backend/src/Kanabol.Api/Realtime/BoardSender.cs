using Microsoft.AspNetCore.SignalR;

namespace Kanabol.Api.Realtime;

/// <summary>
/// The one consumer of BoardOutbox (SPEC section 9): sends each message to its group, in the order they
/// came, so a board sees a task created before it's moved. A send that fails is logged and the next one
/// goes; a socket that doesn't read holds the line at most the hub's send timeout, never a request. When the
/// API stops, the queue takes nothing more and what's in it still goes, for at most 5 seconds.
/// </summary>
public sealed class BoardSender(BoardOutbox queue, IHubContext<BoardHub> hub, ILogger<BoardSender> logger) : BackgroundService
{
    /// <summary>How long a stopping API keeps sending what's left.</summary>
    public static readonly TimeSpan DrainLimit = TimeSpan.FromSeconds(5);

    // Not the token BackgroundService cancels at stop: the loop goes on until the queue is empty, or this.
    private readonly CancellationTokenSource _drainOver = new();

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Close();
        _drainOver.CancelAfter(DrainLimit);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _drainOver.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sending = false;
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(_drainOver.Token))
            {
                sending = true;
                await Send(message, _drainOver.Token);
                sending = false;
            }
        }
        catch (OperationCanceledException) when (_drainOver.IsCancellationRequested)
        {
            // A reader of one can't count what's left, so it takes it out.
            var unsent = sending ? 1 : 0;
            while (queue.Reader.TryRead(out _))
            {
                unsent++;
            }

            logger.LogWarning("Stopped with {UnsentCount} board messages unsent after {DrainLimit}", unsent, DrainLimit);
        }
    }

    private async Task Send(BoardMessage message, CancellationToken ct)
    {
        try
        {
            await hub.Clients.Group(message.Group).SendAsync(message.EventName, message.Payload, ct);
            logger.LogDebug("Sent {BoardEvent} to {BoardGroup}", message.EventName, message.Group);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Couldn't send {BoardEvent} to {BoardGroup}", message.EventName, message.Group);
        }
    }
}
