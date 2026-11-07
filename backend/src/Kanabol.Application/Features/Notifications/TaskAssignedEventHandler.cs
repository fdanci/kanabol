using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Notifications;

/// <summary>
/// The new assignee hears of it, unless they assigned the task to themselves (SPEC section 4.3). It runs
/// once the assignment is saved, for a creation with an assignee too. The message is in English, like
/// the API: the notifications page writes its own Romanian text from the type and the task (group 17).
/// </summary>
public sealed class TaskAssignedEventHandler(
    IAppDbContext db,
    IClock clock,
    ILogger<TaskAssignedEventHandler> logger) : IDomainEventHandler<TaskAssignedEvent>
{
    public async Task Handle(TaskAssignedEvent domainEvent, CancellationToken ct)
    {
        foreach (var assigneeId in domainEvent.AddedIds.Where(addedId => addedId != domainEvent.AssignedById))
        {
            await Notify(assigneeId, domainEvent, ct);
        }
    }

    private async Task Notify(Guid assigneeId, TaskAssignedEvent domainEvent, CancellationToken ct)
    {
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

        var notification = new Notification(
            assigneeId,
            NotificationType.TaskAssigned,
            domainEvent.TaskId,
            $"{assignedBy ?? "Someone"} assigned you {TaskKey.Format(task.Key, task.Number)}: {task.Title}",
            clock.UtcNow);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "User {AssigneeUserId} notified of being assigned task {TaskId}, notification {NotificationId}",
            assigneeId,
            domainEvent.TaskId,
            notification.Id);
    }
}
