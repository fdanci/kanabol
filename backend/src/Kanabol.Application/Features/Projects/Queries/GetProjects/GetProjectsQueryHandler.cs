using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetProjects;

/// <summary>
/// The projects the caller can open: theirs, or every one with projects.view_all or projects.manage,
/// as IProjectAccess decides it, from the database and never for a deactivated user, or only theirs with
/// Mine. Archived ones only when asked for, so an owner can find one to restore.
/// </summary>
public sealed class GetProjectsQueryHandler(IAppDbContext db, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<GetProjectsQuery, Page<ProjectSummaryDto>>
{
    private static readonly string[] _thoseWhoSeeAll = [Permissions.ProjectsViewAll, Permissions.ProjectsManage];

    public async Task<Result<Page<ProjectSummaryDto>>> Handle(GetProjectsQuery query, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var caller = await db.Users
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new
            {
                SeesAll = user.Role.RolePermissions.Any(granted => db.Permissions.Any(
                    permission => permission.Id == granted.PermissionId && _thoseWhoSeeAll.Contains(permission.Name))),
            })
            .FirstOrDefaultAsync(ct);

        if (caller is null)
        {
            return new Page<ProjectSummaryDto>([], query.Page, query.PageSize, 0);
        }

        // IgnoreQueryFilters reaches the tasks too, so the counts below leave archived tasks out themselves.
        var projects = query.IncludeArchived ? db.Projects.IgnoreQueryFilters() : db.Projects;
        if (!caller.SeesAll || query.Mine)
        {
            projects = projects.Where(project => project.Members.Any(member => member.UserId == userId));
        }

        var now = clock.UtcNow;
        var openTasks = db.TaskItems.Where(task => task.ArchivedAt == null
            && !db.BoardColumns.Any(column => column.Id == task.ColumnId && column.IsDone));

        // By name whatever its case. PostgreSQL runs this as upper(), which takes no culture to give.
#pragma warning disable CA1304, CA1311
        var sorted = projects.OrderBy(project => project.Name.ToUpper()).ThenBy(project => project.Key);
#pragma warning restore CA1304, CA1311

        return await sorted
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Key,
                MyRole = project.Members.Where(member => member.UserId == userId).Select(member => (ProjectRole?)member.Role).FirstOrDefault(),
                Owners = db.Users
                    .Where(user => project.Members.Any(member => member.UserId == user.Id && member.Role == ProjectRole.Owner))
                    .OrderBy(user => user.DisplayName)
                    .Select(user => new MemberSummaryDto(user.Id, user.DisplayName))
                    .ToList(),
                MemberPreview = project.Members
                    .Join(db.Users, member => member.UserId, user => user.Id, (member, user) => new { member.Role, user.Id, user.DisplayName })
                    .OrderBy(member => member.Role == ProjectRole.Owner ? 0 : 1)
                    .ThenBy(member => member.DisplayName)
                    .Take(GetProjectsQuery.PreviewedMembers)
                    .Select(member => new MemberSummaryDto(member.Id, member.DisplayName))
                    .ToList(),
                MemberCount = project.Members.Count,
                OpenTaskCount = openTasks.Count(task => task.ProjectId == project.Id),
                OverdueTaskCount = openTasks.Count(task => task.ProjectId == project.Id && task.DueDate < now),
                CompletedTaskCount = db.TaskItems.Count(task => task.ProjectId == project.Id && task.ArchivedAt == null && task.CompletedAt != null),
                ProjectChangedAt = project.UpdatedAt ?? project.CreatedAt,
                TaskChangedAt = db.TaskItems.Where(task => task.ProjectId == project.Id).Max(task => (DateTimeOffset?)(task.UpdatedAt ?? task.CreatedAt)),
                project.ArchivedAt,
            })
            .Select(row => new ProjectSummaryDto(
                row.Id,
                row.Name,
                row.Key,
                row.MyRole,
                row.Owners,
                row.MemberPreview,
                row.MemberCount,
                row.OpenTaskCount,
                row.OverdueTaskCount,
                row.CompletedTaskCount,
                row.TaskChangedAt > row.ProjectChangedAt ? row.TaskChangedAt.Value : row.ProjectChangedAt,
                row.ArchivedAt))
            .ToPage(query.Page, query.PageSize, ct);
    }
}
