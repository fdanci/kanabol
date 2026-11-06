using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskById;

/// <summary>For whoever can see the project: a member, projects.view_all or projects.manage (SPEC section 4.4).</summary>
public sealed class GetTaskByIdQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    ArchivedTasksAccess archivedTasks) : IQueryHandler<GetTaskByIdQuery, TaskItemDto>
{
    public async Task<Result<TaskItemDto>> Handle(GetTaskByIdQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(query.IncludeArchived);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var projectId = await db.Tasks(query.IncludeArchived)
            .Where(task => task.Id == query.Id)
            .Select(task => (Guid?)task.ProjectId)
            .FirstOrDefaultAsync(ct);
        if (projectId is null)
        {
            return TaskErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(projectId.Value, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        return (await db.ReadTask(query.Id, currentUser.Id, query.IncludeArchived, ct))!;
    }
}
