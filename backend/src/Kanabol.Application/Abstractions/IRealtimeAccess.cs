namespace Kanabol.Application.Abstractions;

/// <summary>
/// What the handlers ask of the board hub's open connections (SPEC section 9), once a change that takes
/// access away is saved, so nobody keeps hearing a project until their token expires.
/// </summary>
public interface IRealtimeAccess
{
    /// <summary>Every connection of the user leaves the project's group; their own group stays.</summary>
    Task RemoveFromProjectAsync(Guid userId, Guid projectId, CancellationToken ct);

    /// <summary>Every connection of the user closes: the browser comes back with a fresh token, or not at all.</summary>
    Task DisconnectAsync(Guid userId, CancellationToken ct);
}
