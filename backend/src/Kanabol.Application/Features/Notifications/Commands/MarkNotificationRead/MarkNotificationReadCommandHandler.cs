using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Notifications.Commands.MarkNotificationRead;

/// <summary>
/// Another user's notification answers as one that doesn't exist, 404 Notification.NotFound. Reading it
/// isn't a change of its task: no history, no updated_at.
/// </summary>
public sealed class MarkNotificationReadCommandHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkNotificationReadCommand, NotificationDto>
{
    public async Task<Result<NotificationDto>> Handle(MarkNotificationReadCommand command, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var notification = await db.Notifications
            .FirstOrDefaultAsync(found => found.Id == command.NotificationId && found.UserId == userId, ct);
        if (notification is null)
        {
            return NotificationErrors.NotFound;
        }

        if (notification.MarkRead(clock.UtcNow))
        {
            await db.SaveChangesAsync(ct);
        }

        return await db.Notifications
            .Where(found => found.Id == notification.Id)
            .AsDtos(db)
            .SingleAsync(ct);
    }
}
