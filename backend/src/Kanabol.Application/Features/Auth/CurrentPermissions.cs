using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth;

/// <summary>
/// The second check on what manages users and roles. [HasPermission] reads the token, which can be up
/// to 15 minutes old, and someone who was just deactivated, demoted or had their password reset mustn't
/// be able to use those minutes to make themselves a new account or give their old role back to themselves.
/// </summary>
public sealed class CurrentPermissions(IAppDbContext db, ICurrentUser currentUser, ILogger<CurrentPermissions> logger)
{
    public async Task<Result<HeldPermissions>> Require(string permission, CancellationToken ct)
    {
        var userId = currentUser.Id;
        var caller = await db.Users
            .Where(user => user.Id == userId && user.IsActive)
            .Select(user => new { user.RoleId, user.MustChangePassword })
            .FirstOrDefaultAsync(ct);

        if (caller is null)
        {
            logger.LogWarning("User {RefusedUserId} is gone or deactivated, but their token is still valid", userId);
            return AuthErrors.SessionEnded;
        }

        // A token from before an admin reset the password: it doesn't say the password has to change.
        if (caller.MustChangePassword)
        {
            logger.LogWarning("User {RefusedUserId} has to change their password, but their token is from before", userId);
            return AuthErrors.PasswordChangeRequired;
        }

        var held = await db.PermissionNamesOfRole(caller.RoleId).ToListAsync(ct);
        if (!held.Contains(permission))
        {
            logger.LogWarning("User {RefusedUserId} has a token with {Permission}, but their role no longer has it", userId, permission);
            return AuthErrors.PermissionRequired;
        }

        return new HeldPermissions(held);
    }
}
