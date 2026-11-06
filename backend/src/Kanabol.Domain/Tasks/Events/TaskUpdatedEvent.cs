using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>A field of the task changed: its title, description, priority or deadline. Field is one of TaskHistoryFields.</summary>
public sealed record TaskUpdatedEvent(Guid TaskId, Guid ProjectId, string Field, Guid ChangedById) : IDomainEvent;
