namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// TaskCreated, TaskArchived and TaskRestored: which task, who, and the connection of the tab that did it,
/// so that tab can ignore it. A board that hears it reads itself again (SPEC section 9).
/// </summary>
public sealed record TaskEventDto(Guid TaskId, Guid ProjectId, Guid ByUserId, string? OriginConnectionId);
