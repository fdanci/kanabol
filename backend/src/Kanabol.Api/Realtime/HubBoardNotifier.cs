using Kanabol.Application.Abstractions;

namespace Kanabol.Api.Realtime;

/// <summary>
/// IBoardNotifier over the board hub's groups (SPEC section 9). It runs after the commit, from the domain
/// event handlers, and only queues the message: BoardSender sends it, in order, so the request that made the
/// change never waits for anyone's socket. The change is saved either way, and a board that missed a message
/// reads itself again when it reconnects.
/// </summary>
public sealed class HubBoardNotifier(BoardOutbox queue, ILogger<HubBoardNotifier> logger) : IBoardNotifier
{
    public Task NotifyProjectAsync(Guid projectId, string eventName, object payload, CancellationToken ct)
    {
        Queue(new BoardMessage(BoardGroups.Project(projectId), eventName, payload));
        return Task.CompletedTask;
    }

    public Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct)
    {
        Queue(new BoardMessage(BoardGroups.User(userId), eventName, payload));
        return Task.CompletedTask;
    }

    private void Queue(BoardMessage message)
    {
        if (queue.TryAdd(message))
        {
            logger.LogDebug("Queued {BoardEvent} for {BoardGroup}", message.EventName, message.Group);
        }
        else
        {
            logger.LogDebug("Dropped {BoardEvent} for {BoardGroup}: the API is stopping", message.EventName, message.Group);
        }
    }
}
