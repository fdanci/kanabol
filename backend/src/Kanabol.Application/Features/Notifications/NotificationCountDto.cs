namespace Kanabol.Application.Features.Notifications;

/// <summary>The unread count, or how many read-all marked: { "count": 3 }.</summary>
public sealed record NotificationCountDto(int Count);
