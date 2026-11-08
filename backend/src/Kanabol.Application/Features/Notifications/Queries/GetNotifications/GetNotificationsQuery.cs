using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Notifications;

namespace Kanabol.Application.Features.Notifications.Queries.GetNotifications;

/// <summary>
/// A page of the signed-in user's own notifications, from the newest (SPEC section 4.4, "Notificările"):
/// only the unread ones with Unread, only those types with Types, any of them.
/// </summary>
public sealed record GetNotificationsQuery : IQuery<Page<NotificationDto>>
{
    /// <summary>The bell's menu and the page ask for their own, 10 and 50; the API's default is 20.</summary>
    public const int DefaultPageSize = 20;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = DefaultPageSize;

    public bool Unread { get; init; }

    public IReadOnlyList<NotificationType> Types { get; init; } = [];
}
