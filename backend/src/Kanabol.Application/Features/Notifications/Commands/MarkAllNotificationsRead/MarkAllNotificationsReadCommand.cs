using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>Every unread notification of the signed-in user, read now; it answers how many (SPEC section 4.4).</summary>
public sealed record MarkAllNotificationsReadCommand : ICommand<NotificationCountDto>;
