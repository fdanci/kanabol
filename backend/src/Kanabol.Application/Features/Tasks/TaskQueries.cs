using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Attachments;
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

    /// <summary>
    /// The user's own (SPEC section 4.4, "Panoul principal și task-urile mele"): the tasks they are assigned
    /// to, in projects not archived where they are still a member, and nothing for a deactivated account.
    /// projects.view_all widens nothing. The project filter is written out, as in Tasks.
    /// </summary>
    public static IQueryable<TaskItem> MyTasks(this IAppDbContext db, Guid userId, bool includeArchived) =>
        db.Tasks(includeArchived).Where(task =>
            task.Assignees.Any(assignee => assignee.UserId == userId)
            && db.Projects.IgnoreQueryFilters().Any(project => project.Id == task.ProjectId && project.ArchivedAt == null)
            && db.ProjectMembers.Any(member => member.ProjectId == task.ProjectId && member.UserId == userId)
            && db.Users.Any(user => user.Id == userId && user.IsActive));

    /// <summary>
    /// The task as the caller sees it, in a project archived or not, its worked time counted up to now. Access is the
    /// caller's to check first.
    /// </summary>
    public static async Task<TaskItemDto?> ReadTask(this IAppDbContext db, Guid taskId, ICurrentUser caller, bool includeArchived, WorkedTime workedTime, CancellationToken ct)
    {
        var userId = caller.Id;
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
                    task.EstimatedMinutes,
                    // PostgreSQL runs ToUpper as upper(), which takes no culture to give: the names in any case.
#pragma warning disable CA1304, CA1311
                    Assignees = (
                            from assignee in db.TaskAssignees
                            join user in db.Users on assignee.UserId equals user.Id
                            where assignee.TaskId == task.Id
                            orderby user.DisplayName.ToUpper(), user.Id
                            select new TaskPersonDto(user.Id, user.DisplayName))
                        .ToList(),
#pragma warning restore CA1304, CA1311
                    task.ColumnId,
                    ColumnName = column.Name,
                    task.Position,
                    task.PauseReason,
                    task.WorkedSeconds,
                    task.WorkStartedAt,
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

        if (row is null)
        {
            return null;
        }

        var visible = new VisibleTask(row.Id, row.ProjectId, row.ArchivedAt is not null, row.ProjectArchivedAt is not null, row.IsRealMember);
        var attachments = await db.ReadAttachments(visible, caller, ct);
        return new TaskItemDto(
                row.Id,
                row.ProjectId,
                row.ProjectName,
                TaskKey.Format(row.ProjectKey, row.Number),
                row.Number,
                row.Title,
                row.Description,
                row.Priority,
                row.DueDate,
                row.EstimatedMinutes,
                row.Assignees,
                row.ColumnId,
                row.ColumnName,
                row.Position,
                row.PauseReason,
                await workedTime.SecondsOf(row.WorkedSeconds, row.WorkStartedAt, ct),
                row.WorkStartedAt is not null,
                row.CompletedAt,
                row.ArchivedAt,
                row.CreatedAt,
                row.CreatedById is { } creatorId ? new TaskPersonDto(creatorId, row.CreatorName ?? "") : null,
                row.UpdatedAt,
                attachments,
                visible.CanAddTo);
    }

    /// <summary>Only active members can be given a task (SPEC section 4.4). The ids come each once, which the validators hold.</summary>
    public static async Task<bool> AreAllActiveMembersOf(this IAppDbContext db, Guid projectId, IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return true;
        }

        List<Guid> ids = [.. userIds];
        var found = await db.ProjectMembers.CountAsync(
            member => member.ProjectId == projectId
                && ids.Contains(member.UserId)
                && db.Users.Any(user => user.Id == member.UserId && user.IsActive),
            ct);
        return found == ids.Count;
    }
}
