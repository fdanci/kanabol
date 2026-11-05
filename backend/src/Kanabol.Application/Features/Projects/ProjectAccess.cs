using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects;

/// <summary>
/// Answers from the database, not from ICurrentUser: the user asked about isn't always the one making
/// the request, and a token can still show a permission or an account that's gone. It only answers
/// about access, so an archived project keeps its owners, and whether a project exists is for the
/// handler to find out first.
/// </summary>
public sealed class ProjectAccess(IAppDbContext db) : IProjectAccess
{
    public async Task<Result> EnsureMemberAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var membership = db.ProjectMembers.Where(member => member.ProjectId == projectId && member.UserId == userId);

        return await HasAccess(userId, membership, Permissions.ProjectsViewAll, ct)
            ? Result.Success()
            : ProjectErrors.MemberRequired;
    }

    public async Task<Result> EnsureOwnerAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var ownership = db.ProjectMembers.Where(
            member => member.ProjectId == projectId && member.UserId == userId && member.Role == ProjectRole.Owner);

        return await HasAccess(userId, ownership, Permissions.ProjectsManage, ct)
            ? Result.Success()
            : ProjectErrors.OwnerRequired;
    }

    private Task<bool> HasAccess(Guid userId, IQueryable<ProjectMember> membership, string overridingPermission, CancellationToken ct) =>
        db.Users.AnyAsync(
            user => user.Id == userId
                && user.IsActive
                && (membership.Any() || user.Role.RolePermissions.Any(
                    granted => db.Permissions.Any(
                        permission => permission.Id == granted.PermissionId && permission.Name == overridingPermission))),
            ct);
}
