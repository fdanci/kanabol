using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// Who sees what a task holds, its comments and attachments: whoever sees its project, a member,
/// projects.view_all or projects.manage, as for the task itself. An archived task is there only for
/// tasks.archive, from the token, as GET /api/tasks/{id} with includeArchived (SPEC sections 4.4 and 5.1).
/// </summary>
public sealed class TaskVisibility(IAppDbContext db, IProjectAccess projectAccess, ICurrentUser currentUser)
{
    public async Task<Result<VisibleTask>> Check(Guid taskId, CancellationToken ct)
    {
        var task = await (
                from found in db.TaskItems.IgnoreQueryFilters()
                join project in db.Projects.IgnoreQueryFilters() on found.ProjectId equals project.Id
                where found.Id == taskId
                select new { found.ProjectId, TaskArchived = found.ArchivedAt != null, ProjectArchived = project.ArchivedAt != null })
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);
        if (task is null || (task.TaskArchived && !currentUser.HasPermission(Permissions.TasksArchive)))
        {
            return TaskErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(task.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var realMember = await projectAccess.EnsureRealMemberAsync(task.ProjectId, currentUser.Id, ct);
        return new VisibleTask(taskId, task.ProjectId, task.TaskArchived, task.ProjectArchived, realMember.IsSuccess);
    }
}
