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
    // Whoever can manage any project can also see it (SPEC section 4.4).
    private static readonly string[] _thoseWhoPassAsMember = [Permissions.ProjectsViewAll, Permissions.ProjectsManage];
    private static readonly string[] _thoseWhoPassAsOwner = [Permissions.ProjectsManage];

    public async Task<Result> EnsureMemberAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var membership = db.ProjectMembers.Where(member => member.ProjectId == projectId && member.UserId == userId);

        return await HasAccess(userId, membership, _thoseWhoPassAsMember, ct)
            ? Result.Success()
            : ProjectErrors.MemberRequired;
    }

    public async Task<Result> EnsureOwnerAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var ownership = db.ProjectMembers.Where(
            member => member.ProjectId == projectId && member.UserId == userId && member.Role == ProjectRole.Owner);

        return await HasAccess(userId, ownership, _thoseWhoPassAsOwner, ct)
            ? Result.Success()
            : ProjectErrors.OwnerRequired;
    }

    /// <summary>
    /// For what changes tasks: creating one asks for a real membership, so whoever manages or sees a
    /// project through a permission adds themselves as a member first (SPEC section 4.4).
    /// </summary>
    public async Task<Result> EnsureRealMemberAsync(Guid projectId, Guid userId, CancellationToken ct) =>
        await db.ProjectMembers.AnyAsync(
            member => member.ProjectId == projectId
                && member.UserId == userId
                && db.Users.Any(user => user.Id == userId && user.IsActive),
            ct)
            ? Result.Success()
            : ProjectErrors.MemberRequired;

    /// <summary>
    /// The board's columns (SPEC section 4.4, group 23): an owner, projects.manage, or a real member whose role
    /// has columns.manage, all read here. A refusal is the owner's, as before the permission came.
    /// </summary>
    public async Task<Result> EnsureColumnManagerAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var membership = db.ProjectMembers.Where(member => member.ProjectId == projectId && member.UserId == userId);

        return await db.Users.AnyAsync(
            user => user.Id == userId
                && user.IsActive
                && (membership.Any(member => member.Role == ProjectRole.Owner)
                    || user.Role.RolePermissions.Any(
                        granted => db.Permissions.Any(
                            permission => permission.Id == granted.PermissionId
                                && (permission.Name == Permissions.ProjectsManage
                                    || (permission.Name == Permissions.ColumnsManage && membership.Any()))))),
            ct)
            ? Result.Success()
            : ProjectErrors.OwnerRequired;
    }

    private Task<bool> HasAccess(Guid userId, IQueryable<ProjectMember> membership, string[] overridingPermissions, CancellationToken ct) =>
        db.Users.AnyAsync(
            user => user.Id == userId
                && user.IsActive
                && (membership.Any() || user.Role.RolePermissions.Any(
                    granted => db.Permissions.Any(
                        permission => permission.Id == granted.PermissionId && overridingPermissions.Contains(permission.Name)))),
            ct);
}
