using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Auth.Queries.GetCurrentUser;

/// <summary>Reads the database rather than the token, so the answer is current even if the token isn't.</summary>
public sealed class GetCurrentUserQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<Result<CurrentUserDto>> Handle(GetCurrentUserQuery query, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var user = await db.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId && candidate.IsActive)
            .Select(candidate => new { candidate.Email, candidate.DisplayName, candidate.RoleId, RoleName = candidate.Role.Name, candidate.MustChangePassword })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return AuthErrors.SessionEnded;
        }

        var permissions = await db.PermissionNamesOfRole(user.RoleId).ToListAsync(ct);

        return new CurrentUserDto(userId, user.Email, user.DisplayName, user.RoleName, user.MustChangePassword, permissions);
    }
}
