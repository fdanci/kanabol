using Kanabol.Application.Abstractions;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks;

public static class TaskQueries
{
    /// <summary>
    /// Open tasks, or archived ones too when asked for: whoever asks has passed the check for tasks.archive.
    /// The filter is written out, since these queries join projects archived or not, and IgnoreQueryFilters
    /// anywhere in a query turns off the filter on tasks too.
    /// </summary>
    public static IQueryable<TaskItem> Tasks(this IAppDbContext db, bool includeArchived) =>
        db.TaskItems.IgnoreQueryFilters().Where(task => includeArchived || task.ArchivedAt == null);

    /// <summary>The task as the user given sees it, in a project archived or not. Access is the caller's to check first.</summary>
    public static async Task<TaskItemDto?> ReadTask(this IAppDbContext db, Guid taskId, Guid userId, bool includeArchived, CancellationToken ct)
    {
        var row = await (
                from task in db.Tasks(includeArchived)
                join project in db.Projects.IgnoreQueryFilters() on task.ProjectId equals project.Id
                join column in db.BoardColumns on task.ColumnId equals column.Id
                where task.Id == taskId
                select new
                {
                    task.Id,
                    task.ProjectId,
                    ProjectName = project.Name,
                    ProjectKey = project.Key,
                    ProjectArchivedAt = project.ArchivedAt,
                    task.Number,
                    task.Title,
                    task.Description,
                    task.Priority,
                    task.DueDate,
                    task.AssigneeId,
                    AssigneeName = db.Users.Where(user => user.Id == task.AssigneeId).Select(user => user.DisplayName).FirstOrDefault(),
                    task.ColumnId,
                    ColumnName = column.Name,
                    task.Position,
                    task.CompletedAt,
                    task.ArchivedAt,
                    task.CreatedAt,
                    task.CreatedById,
                    CreatorName = db.Users.Where(user => user.Id == task.CreatedById).Select(user => user.DisplayName).FirstOrDefault(),
                    task.UpdatedAt,
                    IsRealMember = db.ProjectMembers.Any(member => member.ProjectId == task.ProjectId && member.UserId == userId)
                        && db.Users.Any(user => user.Id == userId && user.IsActive),
                })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new TaskItemDto(
                row.Id,
                row.ProjectId,
                row.ProjectName,
                TaskKey.Format(row.ProjectKey, row.Number),
                row.Number,
                row.Title,
                row.Description,
                row.Priority,
                row.DueDate,
                row.AssigneeId is { } assigneeId ? new TaskPersonDto(assigneeId, row.AssigneeName ?? "") : null,
                row.ColumnId,
                row.ColumnName,
                row.Position,
                row.CompletedAt,
                row.ArchivedAt,
                row.CreatedAt,
                row.CreatedById is { } creatorId ? new TaskPersonDto(creatorId, row.CreatorName ?? "") : null,
                row.UpdatedAt,
                row.IsRealMember && row.ProjectArchivedAt is null && row.ArchivedAt is null);
    }

    /// <summary>Only active members can be given a task (Tasks.md group 12).</summary>
    public static Task<bool> IsActiveMemberOf(this IAppDbContext db, Guid projectId, Guid userId, CancellationToken ct) =>
        db.ProjectMembers.AnyAsync(
            member => member.ProjectId == projectId && member.UserId == userId && db.Users.Any(user => user.Id == userId && user.IsActive),
            ct);
}
