namespace Kanabol.Application.Abstractions;

/// <summary>
/// What the request says about where it comes from, beside who sent it (ICurrentUser). Scoped, so the
/// domain event handlers that run after the save read the same request (SPEC section 9).
/// </summary>
public interface IRequestContext
{
    /// <summary>
    /// The SignalR connection of the tab that sent the request, from X-Connection-Id, which the events it
    /// causes carry as originConnectionId so that tab can ignore them. Null without a connection, or
    /// outside a request: then nobody ignores anything.
    /// </summary>
    string? ConnectionId { get; }
}
