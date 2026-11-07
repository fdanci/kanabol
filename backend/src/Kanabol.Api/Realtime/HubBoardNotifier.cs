using Kanabol.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Kanabol.Api.Realtime;

/// <summary>
/// IBoardNotifier over the board hub's groups (SPEC section 9). It runs after the commit, from the domain
/// event handlers, and a failure there is logged and skipped by whoever publishes the events: the change is
/// saved either way, and a board that missed it reads itself again when it reconnects.
/// </summary>
public sealed class HubBoardNotifier(IHubContext<BoardHub> hub, ILogger<HubBoardNotifier> logger) : IBoardNotifier
{
    public async Task NotifyProjectAsync(Guid projectId, string eventName, object payload, CancellationToken ct)
    {
        await hub.Clients.Group(BoardGroups.Project(projectId)).SendAsync(eventName, payload, ct);
        logger.LogDebug("Sent {BoardEvent} to the board of project {ProjectId}", eventName, projectId);
    }

    public async Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct)
    {
        await hub.Clients.Group(BoardGroups.User(userId)).SendAsync(eventName, payload, ct);
        logger.LogDebug("Sent {BoardEvent} to user {NotifiedUserId}", eventName, userId);
    }
}
