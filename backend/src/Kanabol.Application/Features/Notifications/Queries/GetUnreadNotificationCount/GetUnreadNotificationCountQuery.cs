using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Notifications.Queries.GetUnreadNotificationCount;

/// <summary>How many of the signed-in user's notifications are unread: the bell's badge (UI spec section 7.6).</summary>
public sealed record GetUnreadNotificationCountQuery : IQuery<NotificationCountDto>;
