using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Notifications;

namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// A new notification reaches every connection of its user, for the bell in the navbar (SPEC section 9),
/// whoever made it: an assignment, a comment, or the deadline worker.
/// </summary>
public sealed class NotificationCreatedEventHandler(IBoardNotifier notifier) : IDomainEventHandler<NotificationCreatedEvent>
{
    public Task Handle(NotificationCreatedEvent domainEvent, CancellationToken ct) =>
        notifier.NotifyUserAsync(domainEvent.UserId, BoardEvents.NotificationCreated, new NotificationCreatedDto(domainEvent.NotificationId), ct);
}
