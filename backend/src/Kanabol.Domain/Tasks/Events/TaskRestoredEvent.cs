using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

public sealed record TaskRestoredEvent(Guid TaskId, Guid ProjectId, Guid RestoredById) : IDomainEvent;
