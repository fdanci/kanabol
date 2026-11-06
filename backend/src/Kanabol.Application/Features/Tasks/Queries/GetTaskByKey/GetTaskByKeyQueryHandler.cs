using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskByKey;

/// <summary>
/// A key that isn't one, or that no task has, is not found alike. Projects are looked for archived or
/// not, since an archived project still opens; its tasks follow the usual rule for archived ones.
/// </summary>
public sealed class GetTaskByKeyQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    ArchivedTasksAccess archivedTasks) : IQueryHandler<GetTaskByKeyQuery, TaskItemDto>
{
    public async Task<Result<TaskItemDto>> Handle(GetTaskByKeyQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(query.IncludeArchived);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (!TaskKey.TryParse(query.Key, out var projectKey, out var number))
        {
            return TaskErrors.NotFound;
        }

        var found = await (
                from task in db.Tasks(query.IncludeArchived)
                join project in db.Projects.IgnoreQueryFilters() on task.ProjectId equals project.Id
                where project.Key == projectKey && task.Number == number
                select new { task.Id, task.ProjectId })
            .FirstOrDefaultAsync(ct);
        if (found is null)
        {
            return TaskErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(found.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        return (await db.ReadTask(found.Id, currentUser.Id, query.IncludeArchived, ct))!;
    }
}
