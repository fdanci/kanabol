using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Roles.Queries.GetRoleById;

public sealed class GetRoleByIdQueryHandler(IAppDbContext db, CurrentPermissions currentPermissions) : IQueryHandler<GetRoleByIdQuery, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(GetRoleByIdQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var role = await db.Roles.Where(existing => existing.Id == query.Id).ToDtos(db).FirstOrDefaultAsync(ct);
        return role is null ? RoleErrors.NotFound : role;
    }
}
