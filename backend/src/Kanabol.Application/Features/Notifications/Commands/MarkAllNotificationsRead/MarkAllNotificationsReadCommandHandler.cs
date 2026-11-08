using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Notifications.Commands.MarkAllNotificationsRead;

/// <summary>
/// One UPDATE of the caller's unread notifications: those read before keep their time, and the count is of
/// those this one marked, not of those another request marked a moment before.
/// </summary>
public sealed class MarkAllNotificationsReadCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkAllNotificationsReadCommand, NotificationCountDto>
{
    public async Task<Result<NotificationCountDto>> Handle(MarkAllNotificationsReadCommand command, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var now = clock.UtcNow;
        var marked = await db.Notifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(notification => notification.ReadAt, now), ct);
        return new NotificationCountDto(marked);
    }
}
