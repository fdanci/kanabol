using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Microsoft.AspNetCore.SignalR;

namespace Kanabol.Api.Realtime;

/// <summary>
/// The board hub's open connections, in this API's memory (one API, SPEC section 4.3): each user's
/// connections and the projects each joined (SPEC section 9). It holds the hub's limits, 8 projects on a
/// connection and 5 refused joins, and lets the handlers take someone out of a project's group, or close
/// their connections, through IRealtimeAccess.
///
/// A connection's joins, leaves and removals take turns, so a removal that comes while a join reads the
/// database waits for it, then takes the connection out again: nobody stays in a group they lost.
/// </summary>
public sealed class RealtimeConnections(IHubContext<BoardHub> hub, ILogger<RealtimeConnections> logger) : IRealtimeAccess
{
    public const int MaxProjectsPerConnection = 8;
    public const int MaxRefusalsPerConnection = 5;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, Dictionary<string, Connection>> _users = [];

    /// <summary>A join refused, with the connection's refusals so far, or no refusal.</summary>
    public readonly record struct JoinOutcome(Error? Refusal, int Refusals);

    public void Add(Guid userId, HubCallerContext context)
    {
        lock (_lock)
        {
            if (!_users.TryGetValue(userId, out var connections))
            {
                connections = [];
                _users[userId] = connections;
            }

            connections[context.ConnectionId] = new Connection(context);
        }
    }

    public void Remove(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            if (_users.TryGetValue(userId, out var connections) && connections.Remove(connectionId) && connections.Count == 0)
            {
                _users.Remove(userId);
            }
        }
    }

    /// <summary>How many connections the user has open.</summary>
    public int CountOf(Guid userId)
    {
        lock (_lock)
        {
            return _users.TryGetValue(userId, out var connections) ? connections.Count : 0;
        }
    }

    /// <summary>
    /// Joins the project's group when the access check passes. A project joined already is joined again
    /// without asking; a ninth is refused without asking, so without a read of the database.
    /// </summary>
    public async Task<JoinOutcome> Join(Guid userId, string connectionId, Guid projectId, Func<CancellationToken, Task<Result>> access, CancellationToken ct)
    {
        // The hub adds a connection before any of its calls, and removes it after the last.
        var connection = Find(userId, connectionId)
            ?? throw new InvalidOperationException($"Connection {connectionId} isn't registered.");

        await connection.Turn.WaitAsync(ct);
        try
        {
            if (connection.Projects.Contains(projectId))
            {
                return new JoinOutcome(null, connection.Refusals);
            }

            if (connection.Projects.Count >= MaxProjectsPerConnection)
            {
                return new JoinOutcome(HubErrors.TooManyProjects, ++connection.Refusals);
            }

            var allowed = await access(ct);
            if (allowed.IsFailure)
            {
                return new JoinOutcome(allowed.Error, ++connection.Refusals);
            }

            await hub.Groups.AddToGroupAsync(connectionId, BoardGroups.Project(projectId), ct);
            connection.Projects.Add(projectId);
            return new JoinOutcome(null, connection.Refusals);
        }
        finally
        {
            connection.Turn.Release();
        }
    }

    public async Task Leave(Guid userId, string connectionId, Guid projectId, CancellationToken ct)
    {
        var connection = Find(userId, connectionId);
        if (connection is null)
        {
            return;
        }

        await connection.Turn.WaitAsync(ct);
        try
        {
            connection.Projects.Remove(projectId);
            await hub.Groups.RemoveFromGroupAsync(connectionId, BoardGroups.Project(projectId), ct);
        }
        finally
        {
            connection.Turn.Release();
        }
    }

    public async Task RemoveFromProjectAsync(Guid userId, Guid projectId, CancellationToken ct)
    {
        var removed = 0;
        foreach (var connection in ConnectionsOf(userId))
        {
            await connection.Turn.WaitAsync(ct);
            try
            {
                if (connection.Projects.Remove(projectId))
                {
                    await hub.Groups.RemoveFromGroupAsync(connection.Context.ConnectionId, BoardGroups.Project(projectId), ct);
                    removed++;
                }
            }
            finally
            {
                connection.Turn.Release();
            }
        }

        logger.LogInformation(
            "User {UserId} taken out of the board of project {ProjectId} on {ConnectionCount} connections",
            userId,
            projectId,
            removed);
    }

    public Task DisconnectAsync(Guid userId, CancellationToken ct)
    {
        var connections = ConnectionsOf(userId);
        foreach (var connection in connections)
        {
            connection.Context.Abort();
        }

        logger.LogInformation("Closed the {ConnectionCount} connections of user {UserId} to the board hub", connections.Count, userId);
        return Task.CompletedTask;
    }

    private Connection? Find(Guid userId, string connectionId)
    {
        lock (_lock)
        {
            return _users.TryGetValue(userId, out var connections) && connections.TryGetValue(connectionId, out var connection)
                ? connection
                : null;
        }
    }

    private List<Connection> ConnectionsOf(Guid userId)
    {
        lock (_lock)
        {
            return _users.TryGetValue(userId, out var connections) ? [.. connections.Values] : [];
        }
    }

    private sealed class Connection(HubCallerContext context)
    {
        public HubCallerContext Context { get; } = context;

        /// <summary>Joins, leaves and removals on this connection, one at a time.</summary>
        public SemaphoreSlim Turn { get; } = new(1, 1);

        public HashSet<Guid> Projects { get; } = [];

        public int Refusals { get; set; }
    }
}
