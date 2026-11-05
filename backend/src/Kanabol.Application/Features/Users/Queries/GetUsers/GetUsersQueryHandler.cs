using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;

namespace Kanabol.Application.Features.Users.Queries.GetUsers;

public sealed class GetUsersQueryHandler(IAppDbContext db, CurrentPermissions currentPermissions) : IQueryHandler<GetUsersQuery, Page<UserDto>>
{
    public async Task<Result<Page<UserDto>>> Handle(GetUsersQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.UsersManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        return await db.Users
            .OrderBy(user => user.Email)
            .ToDtos()
            .ToPage(query.Page, query.PageSize, ct);
    }
}
