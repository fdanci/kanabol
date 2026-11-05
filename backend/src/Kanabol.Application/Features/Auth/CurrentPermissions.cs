using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth;

/// <summary>
/// The second check on what manages users and roles. [HasPermission] reads the token, which can be up
/// to 15 minutes old, and someone who was just deactivated or demoted mustn't be able to use those
/// minutes to make themselves a new account or give their old role back to themselves.
/// </summary>
public sealed class CurrentPermissions(IAppDbContext db, ICurrentUser currentUser, ILogger<CurrentPermissions> logger)
{
    public async Task<Result<HeldPermissions>> Require(string permission, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var roleId = await db.Users
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => (Guid?)user.RoleId)
            .FirstOrDefaultAsync(ct);

        if (roleId is null)
        {
            logger.LogWarning("User {RefusedUserId} is gone or deactivated, but their token is still valid", userId);
            return AuthErrors.SessionEnded;
        }

        var held = await db.PermissionNamesOfRole(roleId.Value).ToListAsync(ct);
        if (!held.Contains(permission))
        {
            logger.LogWarning("User {RefusedUserId} has a token with {Permission}, but their role no longer has it", userId, permission);
            return AuthErrors.PermissionRequired;
        }

        return new HeldPermissions(held);
    }
}
