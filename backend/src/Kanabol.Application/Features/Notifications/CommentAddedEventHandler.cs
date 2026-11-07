using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Notifications;

/// <summary>
/// A comment tells the task's assignees and its creator, each once, but not its author (SPEC section 4.3),
/// all in one save. Only those who can still open the task hear of it, active members of the project: the
/// message carries the task's title. It's in English, like the API, and the same for each, as TaskAssigned.
/// </summary>
public sealed class CommentAddedEventHandler(
    IAppDbContext db,
    IClock clock,
    ILogger<CommentAddedEventHandler> logger) : IDomainEventHandler<CommentAddedEvent>
{
    public async Task Handle(CommentAddedEvent domainEvent, CancellationToken ct)
    {
        var task = await (
                from found in db.TaskItems.IgnoreQueryFilters()
                join project in db.Projects.IgnoreQueryFilters() on found.ProjectId equals project.Id
                where found.Id == domainEvent.TaskId
                select new { found.Number, found.Title, project.Key, found.CreatedById })
            .FirstAsync(ct);
        var assigneeIds = await db.TaskAssignees
            .Where(assignee => assignee.TaskId == domainEvent.TaskId)
            .Select(assignee => assignee.UserId)
            .ToListAsync(ct);
        var followers = assigneeIds.ToHashSet();
        if (task.CreatedById is { } creatorId)
        {
            followers.Add(creatorId);
        }

        followers.Remove(domainEvent.AuthorId);
        if (followers.Count == 0)
        {
            return;
        }

        List<Guid> candidates = [.. followers];
        var notified = await db.ProjectMembers
            .Where(member => member.ProjectId == domainEvent.ProjectId
                && candidates.Contains(member.UserId)
                && db.Users.Any(user => user.Id == member.UserId && user.IsActive))
            .Select(member => member.UserId)
            .ToListAsync(ct);
        if (notified.Count == 0)
        {
            return;
        }

        var author = await db.Users
            .Where(user => user.Id == domainEvent.AuthorId)
            .Select(user => user.DisplayName)
            .FirstOrDefaultAsync(ct);

        var message = $"{author ?? "Someone"} commented on {TaskKey.Format(task.Key, task.Number)}: {task.Title}";
        var now = clock.UtcNow;
        Notification[] notifications =
        [
            .. notified.Select(userId => new Notification(userId, NotificationType.CommentAdded, domainEvent.TaskId, message, now)),
        ];
        db.Notifications.AddRange(notifications);
        await db.SaveChangesAsync(ct);

        foreach (var notification in notifications)
        {
            logger.LogInformation(
                "User {NotifiedUserId} notified of comment {CommentId} on task {TaskId}, notification {NotificationId}",
                notification.UserId,
                domainEvent.CommentId,
                domainEvent.TaskId,
                notification.Id);
        }
    }
}
