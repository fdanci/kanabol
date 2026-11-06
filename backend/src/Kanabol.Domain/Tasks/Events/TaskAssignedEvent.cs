using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>The task has another assignee, or none when AssigneeId is null.</summary>
public sealed record TaskAssignedEvent(Guid TaskId, Guid ProjectId, Guid? AssigneeId, Guid? PreviousAssigneeId, Guid AssignedById) : IDomainEvent;
