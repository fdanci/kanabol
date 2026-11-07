using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

/// <summary>
/// For whoever can see the project, archived or not (SPEC section 4.4). Every open task, without pages:
/// the board shows them all, and its filters work in the browser, so a card dropped among filtered ones
/// still lands in its true place.
/// </summary>
public sealed class GetBoardQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetBoardQuery, BoardDto>
{
    public async Task<Result<BoardDto>> Handle(GetBoardQuery query, CancellationToken ct)
    {
        var project = await db.Projects.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(found => found.Id == query.ProjectId)
            .Select(found => new { found.Key, found.ArchivedAt })
            .FirstOrDefaultAsync(ct);
        if (project is null)
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var open = project.ArchivedAt is null;
        var canMove = open && (await projectAccess.EnsureRealMemberAsync(query.ProjectId, currentUser.Id, ct)).IsSuccess;
        var canManage = open && (await projectAccess.EnsureOwnerAsync(query.ProjectId, currentUser.Id, ct)).IsSuccess;

        var columns = await db.ColumnsOf(query.ProjectId).ToArrayAsync(ct);
        var rows = await db.TaskItems
            .AsNoTracking()
            .Where(task => task.ProjectId == query.ProjectId)
            .OrderBy(task => task.Position)
            .ThenBy(task => task.Number)
            .Select(task => new
            {
                task.Id,
                task.Number,
                task.Title,
                task.ColumnId,
                task.Position,
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
            })
            .ToListAsync(ct);

        return new BoardDto(
            query.ProjectId,
            project.Key,
            !open,
            canMove,
            canManage,
            columns,
            [.. rows.Select(row => new BoardTaskDto(
                row.Id,
                TaskKey.Format(project.Key, row.Number),
                row.Number,
                row.Title,
                row.ColumnId,
                row.Position,
                row.Priority,
                row.DueDate,
                row.Assignees.FirstOrDefault(),
                row.CompletedAt,
                row.CommentCount,
                row.AttachmentCount))]);
    }
}
