using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Notifications.Commands.MarkNotificationRead;

/// <summary>One of the signed-in user's notifications, read; a second time keeps the first (SPEC section 4.4).</summary>
public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand<NotificationDto>;
