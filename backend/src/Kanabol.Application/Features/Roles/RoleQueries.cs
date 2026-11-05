using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Roles;

public static class RoleQueries
{
    public static IQueryable<RoleDto> ToDtos(this IQueryable<Role> roles, IAppDbContext db) =>
        roles.Select(role => new RoleDto(
            role.Id,
            role.Name,
            role.IsSystem,
            role.RolePermissions
                .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name)
                .OrderBy(name => name)
                .ToList(),
            db.Users.Count(user => user.RoleId == role.Id)));

    /// <summary>
    /// Ignores case, because a second "admin" next to the system role would read as the same thing. In
    /// memory, since there are few roles and this way it doesn't depend on how the database folds case.
    /// </summary>
    public static async Task<bool> IsNameTaken(this IAppDbContext db, string name, Guid? exceptRoleId, CancellationToken ct)
    {
        var names = await db.Roles.Where(role => role.Id != exceptRoleId).Select(role => role.Name).ToListAsync(ct);

        return names.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
