using System.Security.Claims;
using Kanabol.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kanabol.Api.Realtime;

/// <summary>
/// The hub the boards listen to (SPEC section 9). It only sends: changes go through the API, and
/// IBoardNotifier tells the groups. Every connection joins its user's group; a project's group is only for
/// whoever IProjectAccess lets see the project, asked when joining, at most 8 projects on a connection.
/// RealtimeConnections keeps each user's connections and their projects, so a handler that takes access
/// away takes the connections out of the project's group, or closes them (IRealtimeAccess); a connection
/// also closes when its token expires. The token comes from the query string (SPEC section 4.5,
/// JwtAuthenticationExtensions).
/// </summary>
[Authorize]
public sealed class BoardHub(IProjectAccess projectAccess, RealtimeConnections connections, ILogger<BoardHub> logger) : Hub
{
    public const string Path = "/hubs/board";

    private Guid UserId => Guid.Parse(Context.User!.FindFirstValue(AuthClaims.Subject)!);

    public override async Task OnConnectedAsync()
    {
        connections.Add(UserId, Context);
        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroups.User(UserId), Context.ConnectionAborted);
        logger.LogInformation("User {UserId} connected to the board hub on {ConnectionId}", UserId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connections.Remove(UserId, Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The project's changes, for a member, projects.view_all or projects.manage, as the project's GET asks.
    /// Answers null for a join, and the error's code for a refusal, such as Project.MemberRequired or
    /// Hub.TooManyProjects: a refusal is an ordinary answer, and SignalR would log a method that throws as an
    /// Error. A project that doesn't exist has nobody sending to its group, so it isn't looked for. The fifth
    /// refusal on a connection closes it, since a board joins one project, and only again when it reconnects.
    /// </summary>
    public async Task<string?> JoinProject(Guid projectId)
    {
        var outcome = await connections.Join(
            UserId,
            Context.ConnectionId,
            projectId,
            ct => projectAccess.EnsureMemberAsync(projectId, UserId, ct),
            Context.ConnectionAborted);
        if (outcome.Refusal is null)
        {
            logger.LogInformation(
                "User {UserId} joined the board of project {ProjectId} on {ConnectionId}",
                UserId,
                projectId,
                Context.ConnectionId);
            return null;
        }

        logger.LogWarning(
            "User {UserId} refused the board of project {ProjectId} on {ConnectionId}: {ErrorCode}",
            UserId,
            projectId,
            Context.ConnectionId,
            outcome.Refusal.Code);
        if (outcome.Refusals >= RealtimeConnections.MaxRefusalsPerConnection)
        {
            logger.LogWarning(
                "Closed {ConnectionId} of user {UserId} after {RefusalCount} refused joins",
                Context.ConnectionId,
                UserId,
                outcome.Refusals);
            Context.Abort();
        }

        return outcome.Refusal.Code;
    }

    /// <summary>When the board closes, or another project's opens on the same connection. Frees its place.</summary>
    public Task LeaveProject(Guid projectId) =>
        connections.Leave(UserId, Context.ConnectionId, projectId, Context.ConnectionAborted);
}
