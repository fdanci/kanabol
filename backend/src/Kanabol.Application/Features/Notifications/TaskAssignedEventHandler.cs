using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Notifications;

/// <summary>
/// Each assignee newly added hears of it, not those already on the task, nor whoever made the change
/// (SPEC section 4.3). It runs once the assignment is saved, for a creation with assignees too, and saves
/// all its notifications at once. The message is in English, like the API, and the same for each: the
/// notifications page writes its own Romanian text from the type and the task (group 17).
/// </summary>
public sealed class TaskAssignedEventHandler(
    IAppDbContext db,
    IClock clock,
    ILogger<TaskAssignedEventHandler> logger) : IDomainEventHandler<TaskAssignedEvent>
{
    public async Task Handle(TaskAssignedEvent domainEvent, CancellationToken ct)
    {
        Guid[] notified = [.. domainEvent.AddedIds.Where(addedId => addedId != domainEvent.AssignedById)];
        if (notified.Length == 0)
        {
            return;
        }

        var task = await (
                from found in db.TaskItems.IgnoreQueryFilters()
                join project in db.Projects.IgnoreQueryFilters() on found.ProjectId equals project.Id
                where found.Id == domainEvent.TaskId
                select new { found.Number, found.Title, project.Key })
            .FirstAsync(ct);
        var assignedBy = await db.Users
            .Where(user => user.Id == domainEvent.AssignedById)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(ct);

        var message = $"{assignedBy ?? "Someone"} assigned you {TaskKey.Format(task.Key, task.Number)}: {task.Title}";
        var now = clock.UtcNow;
        Notification[] notifications =
        [
            .. notified.Select(assigneeId => new Notification(assigneeId, NotificationType.TaskAssigned, domainEvent.TaskId, message, now)),
        ];
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(ct);

        foreach (var notification in notifications)
        {
            logger.LogInformation(
                "User {AssigneeUserId} notified of being assigned task {TaskId}, notification {NotificationId}",
                notification.UserId,
                domainEvent.TaskId,
                notification.Id);
        }
    }
}
