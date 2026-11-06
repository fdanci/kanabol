using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Queries.GetProjectMemberCandidates;

/// <summary>
/// An owner without users.manage still has to find whom to add, so this lists active users who aren't
/// in the project yet, the first 20 by name. Only owners: a member has nobody to add.
/// </summary>
public sealed class GetProjectMemberCandidatesQueryHandler(
    IAppDbContext db,
    IProjectAccess projectAccess,
    ICurrentUser currentUser) : IQueryHandler<GetProjectMemberCandidatesQuery, MemberCandidateDto[]>
{
    public async Task<Result<MemberCandidateDto[]>> Handle(GetProjectMemberCandidatesQuery query, CancellationToken ct)
    {
        if (!await db.ProjectExists(query.ProjectId, ct))
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureOwnerAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var candidates = db.Users.Where(user => user.IsActive && !db.ProjectMembers.Any(
            member => member.ProjectId == query.ProjectId && member.UserId == user.Id));

        var search = query.Search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(search))
        {
            // PostgreSQL runs this as upper() and strpos(), which take no culture and no comparison to give.
#pragma warning disable CA1304, CA1311, CA1862
            candidates = candidates.Where(user => user.Email.ToUpper().Contains(search) || user.DisplayName.ToUpper().Contains(search));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await candidates
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Take(GetProjectMemberCandidatesQuery.MaxResults)
            .Select(user => new MemberCandidateDto(user.Id, user.Email, user.DisplayName))
            .AsNoTracking()
            .ToArrayAsync(ct);
    }
}
