using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Notifications.Queries.GetUnreadNotificationCount;

public sealed class GetUnreadNotificationCountQueryHandler(IAppDbContext db, ICurrentUser currentUser)
    : IQueryHandler<GetUnreadNotificationCountQuery, NotificationCountDto>
{
    public async Task<Result<NotificationCountDto>> Handle(GetUnreadNotificationCountQuery query, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var count = await db.Notifications.CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, ct);
        return new NotificationCountDto(count);
    }
}
