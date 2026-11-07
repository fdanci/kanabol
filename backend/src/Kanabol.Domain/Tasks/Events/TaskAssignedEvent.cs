using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>
/// The task's assignees changed: AddedIds came, RemovedIds left, and whoever stayed is in neither. Only
/// the added ones hear of it (SPEC section 4.3). A creation with assignees adds them all.
/// </summary>
public sealed record TaskAssignedEvent(
    Guid TaskId,
    Guid ProjectId,
    IReadOnlyList<Guid> AddedIds,
    IReadOnlyList<Guid> RemovedIds,
    Guid AssignedById) : IDomainEvent;
