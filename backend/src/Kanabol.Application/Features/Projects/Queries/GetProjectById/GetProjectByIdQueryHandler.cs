using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectById;

/// <summary>An archived project still opens, for those who could see it before, so an owner can restore it.</summary>
public sealed class GetProjectByIdQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetProjectByIdQuery, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(GetProjectByIdQuery query, CancellationToken ct)
    {
        if (!await db.ProjectExists(query.Id, ct))
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.Id, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var found = await db.ReadProject(projectAccess, query.Id, currentUser.Id, ct);
        return found is null ? ProjectErrors.NotFound : found;
    }
}
