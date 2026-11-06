using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMember;

/// <summary>Where the Location of an added member points.</summary>
public sealed class GetProjectMemberQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetProjectMemberQuery, ProjectMemberDto>
{
    public async Task<Result<ProjectMemberDto>> Handle(GetProjectMemberQuery query, CancellationToken ct)
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

        var found = await db.MembersOf(query.ProjectId, query.UserId).AsNoTracking().FirstOrDefaultAsync(ct);
        return found is null ? ProjectErrors.MemberNotFound : found;
    }
}
