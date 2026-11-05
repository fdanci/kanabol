using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Users;

/// <summary>
/// The last active user with the system role can't lose it or be deactivated (SPEC section 4.4). A
/// count alone can't promise that, when other requests run at the same moment:
/// - Two requests each taking away one of the last two admins would both see the other one. So letting
///   someone leave also changes the role's HoldersVersion, and the second of two such saves finds the
///   version changed and fails. The version and the count come from one statement, so they agree.
/// - A request could take away someone it read in another role, who became an admin meanwhile. The
///   user's RoleId is a concurrency token, so such a save finds the row changed and fails.
/// </summary>
public sealed class LastAdminGuard(IAppDbContext db)
{
    public async Task<Result> AllowLeaving(User user, CancellationToken ct)
    {
        var current = await db.Roles
            .Where(role => role.Id == user.RoleId)
            .Select(role => new
            {
                Role = role,
                OthersRemain = db.Users.Any(other => other.RoleId == role.Id && other.IsActive && other.Id != user.Id),
            })
            .FirstOrDefaultAsync(ct);

        // The role the user was read in is gone, so they've moved since.
        if (current is null)
        {
            return UserErrors.ChangedMeanwhile;
        }

        if (!current.Role.IsSystem || !user.IsActive)
        {
            return Result.Success();
        }

        if (!current.OthersRemain)
        {
            return UserErrors.LastAdmin;
        }

        current.Role.RecordHolderLeaving();
        return Result.Success();
    }
}
