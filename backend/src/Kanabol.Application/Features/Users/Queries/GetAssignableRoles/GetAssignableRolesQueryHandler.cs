using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Users.Queries.GetAssignableRoles;

/// <summary>What the user form offers. Someone with users.manage but without roles.manage can read only this much about roles.</summary>
public sealed class GetAssignableRolesQueryHandler(IAppDbContext db, CurrentPermissions currentPermissions) : IQueryHandler<GetAssignableRolesQuery, AssignableRoleDto[]>
{
    public async Task<Result<AssignableRoleDto[]>> Handle(GetAssignableRolesQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.UsersManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var roles = await db.Roles
            .OrderBy(role => role.Name)
            .Select(role => new
            {
                role.Id,
                role.Name,
                Permissions = role.RolePermissions
                    .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name)
                    .ToList(),
            })
            .ToListAsync(ct);

        return roles
            .Where(role => held.Value.Covers(role.Permissions))
            .Select(role => new AssignableRoleDto(role.Id, role.Name))
            .ToArray();
    }
}
