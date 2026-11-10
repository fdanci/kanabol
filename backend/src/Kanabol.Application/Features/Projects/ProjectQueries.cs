using Kanabol.Application.Abstractions;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects;

public static class ProjectQueries
{
    /// <summary>The project, archived or not, as the user given sees it. Access is the caller's to check first.</summary>
    public static async Task<ProjectDto?> ReadProject(
        this IAppDbContext db,
        IProjectAccess projectAccess,
        Guid projectId,
        Guid userId,
        CancellationToken ct)
    {
        var found = await db.Projects
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(project => project.Id == projectId)
            .Select(project => new
            {
                project.Id,
                project.Name,
                project.Key,
                project.Description,
                project.CreatedAt,
                project.ArchivedAt,
                MyRole = project.Members.Where(member => member.UserId == userId).Select(member => (ProjectRole?)member.Role).FirstOrDefault(),
                Columns = project.Columns
                    .OrderBy(column => column.Position)
                    .Select(column => new BoardColumnDto(column.Id, column.Name, column.Position, column.Kind))
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (found is null)
        {
            return null;
        }

        var canManage = (await projectAccess.EnsureOwnerAsync(projectId, userId, ct)).IsSuccess;
        return new ProjectDto(found.Id, found.Name, found.Key, found.Description, found.CreatedAt, found.ArchivedAt, found.MyRole, canManage, found.Columns);
    }

    /// <summary>Owners first, then by name. With a user, only that one.</summary>
    public static IQueryable<ProjectMemberDto> MembersOf(this IAppDbContext db, Guid projectId, Guid? userId = null) =>
        db.ProjectMembers
            .Where(member => member.ProjectId == projectId && (userId == null || member.UserId == userId))
            .Join(db.Users, member => member.UserId, user => user.Id, (member, user) => new { member, user })
            .OrderBy(row => row.member.Role == ProjectRole.Owner ? 0 : 1)
            .ThenBy(row => row.user.DisplayName)
            .ThenBy(row => row.user.Email)
            .Select(row => new ProjectMemberDto(row.user.Id, row.user.Email, row.user.DisplayName, row.member.Role, row.user.IsActive));

    /// <summary>
    /// The members whose accounts are active, which the project needs to keep an active owner
    /// (SPEC section 4.4). The project doesn't load its users, so the handler reads them here.
    /// </summary>
    public static Task<List<Guid>> ActiveMemberIdsOf(this IAppDbContext db, Guid projectId, CancellationToken ct) =>
        db.ProjectMembers
            .Where(member => member.ProjectId == projectId)
            .Join(db.Users.Where(user => user.IsActive), member => member.UserId, user => user.Id, (_, user) => user.Id)
            .ToListAsync(ct);

    public static Task<bool> ProjectExists(this IAppDbContext db, Guid projectId, CancellationToken ct) =>
        db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == projectId, ct);
}
