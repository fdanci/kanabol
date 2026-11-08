using Kanabol.Application.Abstractions;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Notifications;

public static class NotificationQueries
{
    /// <summary>
    /// The notifications given, from the newest and, in the same moment, by id from the last, so none is on
    /// two pages; each with its task as it is now, archived or not, in a project archived or not.
    /// </summary>
    public static IQueryable<NotificationDto> AsDtos(this IQueryable<Notification> notifications, IAppDbContext db) =>
        from notification in notifications
        join task in db.TaskItems.IgnoreQueryFilters() on notification.TaskId equals task.Id into tasks
        from task in tasks.DefaultIfEmpty()
        join project in db.Projects.IgnoreQueryFilters() on task!.ProjectId equals project.Id into projects
        from project in projects.DefaultIfEmpty()
        orderby notification.CreatedAt descending, notification.Id descending
        select new NotificationDto(
            notification.Id,
            notification.Type,
            notification.Message,
            notification.CreatedAt,
            notification.ReadAt,
            task == null || project == null ? null : new NotificationTaskDto(task.Id, TaskKey.Format(project.Key, task.Number), task.Title));
}
