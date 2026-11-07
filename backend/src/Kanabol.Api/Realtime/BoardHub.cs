using System.Security.Claims;
using Kanabol.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kanabol.Api.Realtime;

/// <summary>
/// The hub the boards listen to (SPEC section 9). It only sends: changes go through the API, and
/// IBoardNotifier tells the groups. Every connection joins its user's group; a project's group is only for
/// whoever IProjectAccess lets see the project, asked when joining, so a connection opened before someone
/// lost access keeps hearing the ids of that project's changes until it closes, and it closes when its token
/// expires. The token comes from the query string (SPEC section 4.5, JwtAuthenticationExtensions).
/// </summary>
[Authorize]
public sealed class BoardHub(IProjectAccess projectAccess, ILogger<BoardHub> logger) : Hub
{
    public const string Path = "/hubs/board";

    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(AuthClaims.Subject)!);

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.User(UserId), Context.ConnectionAborted);
        logger.LogInformation("User {UserId} connected to the board hub on {ConnectionId}", UserId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// The project's changes, for a member, projects.view_all or projects.manage, as the project's GET asks.
    /// A refusal throws a HubException whose message ends with the error's code. A project that doesn't
    /// exist has nobody sending to its group, so it isn't looked for.
    /// </summary>
    public async Task JoinProject(Guid projectId)
    {
        var access = await projectAccess.EnsureMemberAsync(projectId, UserId, Context.ConnectionAborted);
        if (access.IsFailure)
        {
            logger.LogWarning(
                "User {UserId} refused the board of project {ProjectId} on {ConnectionId}: {ErrorCode}",
                UserId,
                projectId,
                Context.ConnectionId,
                access.Error.Code);
            throw new HubException(access.Error.Code);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.Project(projectId), Context.ConnectionAborted);
        logger.LogInformation(
            "User {UserId} joined the board of project {ProjectId} on {ConnectionId}",
            UserId,
            projectId,
            Context.ConnectionId);
    }

    /// <summary>When the board closes, or another project's opens on the same connection.</summary>
    public Task LeaveProject(Guid projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardGroups.Project(projectId), Context.ConnectionAborted);
}
