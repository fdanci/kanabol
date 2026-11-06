using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

/// <summary>The task went to another column, or to another place in its own.</summary>
public sealed record TaskMovedEvent(Guid TaskId, Guid ProjectId, Guid FromColumnId, Guid ToColumnId, int Position, Guid MovedById) : IDomainEvent;
