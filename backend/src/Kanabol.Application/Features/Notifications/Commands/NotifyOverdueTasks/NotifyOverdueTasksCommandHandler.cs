using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;

/// <summary>
/// Every task past its deadline, not completed, not archived, in a project that isn't archived, tells its
/// assignees and the project's owners, those of them who are active members, as CommentAdded does (SPEC
/// section 4.3). Each person hears of a task once, ever: someone assigned later to a task already overdue
/// hears of it at the next run, and nobody hears again when the deadline moves away and passes once more.
/// All of a run's notifications go in one save, and each reaches its user's group through
/// NotificationCreatedEvent. The message is in English, like the API's others.
/// </summary>
public sealed class NotifyOverdueTasksCommandHandler(
    IAppDbContext db,
    IClock clock,
    ILogger<NotifyOverdueTasksCommandHandler> logger) : ICommandHandler<NotifyOverdueTasksCommand, int>
{
    public async Task<Result<int>> Handle(NotifyOverdueTasksCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var overdue =
            from task in db.TaskItems
            join project in db.Projects on task.ProjectId equals project.Id
            where task.DueDate < now && task.CompletedAt == null && task.ArchivedAt == null && project.ArchivedAt == null
            select new { task.Id, task.ProjectId, task.Number, task.Title, project.Key };

        // project_members holds a person once per project, so each one comes once per task.
        var due = await (
                from task in overdue
                from member in db.ProjectMembers
                where member.ProjectId == task.ProjectId
                    && (member.Role == ProjectRole.Owner
                        || db.TaskAssignees.Any(assignee => assignee.TaskId == task.Id && assignee.UserId == member.UserId))
                    && db.Users.Any(user => user.Id == member.UserId && user.IsActive)
                    && !db.Notifications.Any(notification => notification.Type == NotificationType.TaskOverdue
                        && notification.TaskId == task.Id
                        && notification.UserId == member.UserId)
                orderby task.Key, task.Number, member.UserId
                select new { TaskId = task.Id, member.UserId, task.Key, task.Number, task.Title })
            .ToListAsync(ct);

        Notification[] notifications =
        [
            .. due.Select(pair => new Notification(
                pair.UserId,
                NotificationType.TaskOverdue,
                pair.TaskId,
                $"{TaskKey.Format(pair.Key, pair.Number)}: {pair.Title} is overdue",
                now)),
        ];
        if (notifications.Length > 0)
        {
            db.Notifications.AddRange(notifications);
            await db.SaveChangesAsync(ct);
        }

        foreach (var notification in notifications)
        {
            logger.LogDebug(
                "User {NotifiedUserId} notified that task {TaskId} is overdue, notification {NotificationId}",
                notification.UserId,
                notification.TaskId,
                notification.Id);
        }

        logger.LogInformation(
            "Overdue check made {NotificationCount} notifications for {TaskCount} tasks",
            notifications.Length,
            due.Select(pair => pair.TaskId).Distinct().Count());
        return notifications.Length;
    }
}
