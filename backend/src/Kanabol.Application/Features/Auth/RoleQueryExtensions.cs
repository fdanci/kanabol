using Kanabol.Application.Abstractions;

namespace Kanabol.Application.Features.Auth;

public static class RoleQueryExtensions
{
    public static IQueryable<string> PermissionNamesOfRole(this IAppDbContext db, Guid roleId) =>
        db.Roles
            .Where(role => role.Id == roleId)
            .SelectMany(role => role.RolePermissions)
            .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name);
}
