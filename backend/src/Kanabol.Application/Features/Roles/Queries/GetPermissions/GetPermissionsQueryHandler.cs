using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;

namespace Kanabol.Application.Features.Roles.Queries.GetPermissions;

public sealed class GetPermissionsQueryHandler(CurrentPermissions currentPermissions) : IQueryHandler<GetPermissionsQuery, string[]>
{
    public async Task<Result<string[]>> Handle(GetPermissionsQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        return Permissions.All.ToArray();
    }
}
