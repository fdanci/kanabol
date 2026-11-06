using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

public sealed record TaskCreatedEvent(Guid TaskId, Guid ProjectId, int Number, Guid CreatedById) : IDomainEvent;
