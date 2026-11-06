using Kanabol.Domain.Common;

namespace Kanabol.Domain.Notifications;

/// <summary>A notification for UserId: the bell's count and list (SPEC sections 4.3 and 9).</summary>
public sealed record NotificationCreatedEvent(Guid NotificationId, Guid UserId) : IDomainEvent;
