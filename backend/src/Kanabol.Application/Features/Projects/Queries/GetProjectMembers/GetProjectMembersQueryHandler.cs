using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMembers;

/// <summary>Every member, deactivated ones too, who still hold their place until an owner removes them.</summary>
public sealed class GetProjectMembersQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetProjectMembersQuery, ProjectMemberDto[]>
{
    public async Task<Result<ProjectMemberDto[]>> Handle(GetProjectMembersQuery query, CancellationToken ct)
    {
        if (!await db.ProjectExists(query.ProjectId, ct))
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        return await db.MembersOf(query.ProjectId).AsNoTracking().ToArrayAsync(ct);
    }
}
