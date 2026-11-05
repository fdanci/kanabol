using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Users.Queries.GetUserById;

public sealed class GetUserByIdQueryHandler(IAppDbContext db, CurrentPermissions currentPermissions) : IQueryHandler<GetUserByIdQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetUserByIdQuery query, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.UsersManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var found = await db.Users.Where(user => user.Id == query.Id).ToDtos().FirstOrDefaultAsync(ct);
        return found is null ? UserErrors.NotFound : found;
    }
}
