using Kanabol.Domain.Common;

namespace Kanabol.Application.Abstractions.Messaging;

public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
