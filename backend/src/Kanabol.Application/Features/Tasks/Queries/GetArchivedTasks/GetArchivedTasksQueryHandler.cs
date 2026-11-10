using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetArchivedTasks;

/// <summary>
/// The column "Arhivate" (SPEC section 4.4): tasks.archive, from the token, as for includeArchived, then seeing
/// the project, a member, projects.view_all or projects.manage, in a project archived or not; a project the
/// caller doesn't see answers as one that doesn't exist, 404. From the latest archiving, those of one moment by
/// their id, so no card is on two pages.
/// </summary>
public sealed class GetArchivedTasksQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser,
    ArchivedTasksAccess archivedTasks) : IQueryHandler<GetArchivedTasksQuery, Page<ArchivedTaskDto>>
{
    public async Task<Result<Page<ArchivedTaskDto>>> Handle(GetArchivedTasksQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(includeArchived: true);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var key = await db.Projects.IgnoreQueryFilters()
            .Where(found => found.Id == query.ProjectId)
            .Select(found => found.Key)
            .FirstOrDefaultAsync(ct);
        if (key is null || (await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct)).IsFailure)
        {
            return ProjectErrors.NotFound;
        }

        var page = await (
                from task in db.Tasks(includeArchived: true)
                join column in db.BoardColumns on task.ColumnId equals column.Id
                where task.ProjectId == query.ProjectId && task.ArchivedAt != null
                orderby task.ArchivedAt descending, task.Id descending
                select new
                {
                    task.Id,
                    task.Number,
                    task.Title,
                    task.ColumnId,
                    ColumnName = column.Name,
                    task.Priority,
                    task.DueDate,
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
                    task.CompletedAt,
                    CommentCount = db.Comments.Count(comment => comment.TaskId == task.Id),
                    AttachmentCount = db.Attachments.Count(attachment => attachment.TaskId == task.Id),
                    task.EstimatedMinutes,
                    task.WorkedSeconds,
                    task.PauseReason,
                    ArchivedAt = task.ArchivedAt!.Value,
                    ArchivedBy = db.Users
                        .Where(user => user.Id == task.ArchivedById)
                        .Select(user => new TaskPersonDto(user.Id, user.DisplayName))
                        .FirstOrDefault(),
                })
            .AsNoTracking()
            .ToPage(query.Page, query.PageSize, ct);

        return new Page<ArchivedTaskDto>(
            [.. page.Items.Select(row => new ArchivedTaskDto(
                row.Id,
                TaskKey.Format(key, row.Number),
                row.Number,
                row.Title,
                row.ColumnId,
                row.ColumnName,
                row.Priority,
                row.DueDate,
                row.Assignees,
                row.CompletedAt,
                row.CommentCount,
                row.AttachmentCount,
                row.EstimatedMinutes,
                row.WorkedSeconds,
                IsWorking: false,
                row.PauseReason,
                row.ArchivedAt,
                row.ArchivedBy))],
            page.Number,
            page.PageSize,
            page.TotalCount);
    }
}
