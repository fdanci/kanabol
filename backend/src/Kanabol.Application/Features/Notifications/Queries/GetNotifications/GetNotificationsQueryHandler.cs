using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Notifications.Queries.GetNotifications;

/// <summary>
/// The caller's own, and nobody else's, without any permission. A notification stays in the list when its
/// user can no longer open its task: opening the task answers 403 or 404, as it would anyway.
/// </summary>
public sealed class GetNotificationsQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetNotificationsQuery, Page<NotificationDto>>
{
    public async Task<Result<Page<NotificationDto>>> Handle(GetNotificationsQuery query, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var notifications = db.Notifications.Where(notification => notification.UserId == userId);
        if (query.Unread)
        {
            notifications = notifications.Where(notification => notification.ReadAt == null);
        }

        if (query.Types.Count > 0)
        {
            var types = query.Types.Distinct().ToList();
            notifications = notifications.Where(notification => types.Contains(notification.Type));
        }

        return await notifications.AsDtos(db).ToPage(query.Page, query.PageSize, ct);
    }
}
