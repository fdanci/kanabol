using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks.Events;

public sealed record TaskArchivedEvent(Guid TaskId, Guid ProjectId, Guid ArchivedById) : IDomainEvent;
