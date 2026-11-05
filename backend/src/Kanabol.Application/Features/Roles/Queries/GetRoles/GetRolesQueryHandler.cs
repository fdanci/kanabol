using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;

namespace Kanabol.Application.Features.Roles.Queries.GetRoles;

public sealed class GetRolesQueryHandler(IAppDbContext db, CurrentPermissions currentPermissions) : IQueryHandler<GetRolesQuery, Page<RoleDto>>
{
    public async Task<Result<Page<RoleDto>>> Handle(GetRolesQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        return await db.Roles
            .OrderBy(role => role.Name)
            .ThenBy(role => role.Id)
            .ToDtos(db)
            .ToPage(query.Page, query.PageSize, ct);
    }
}
