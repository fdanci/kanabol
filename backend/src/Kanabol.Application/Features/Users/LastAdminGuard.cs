using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Users;

/// <summary>
/// The last active user with the system role can't lose it or be deactivated (SPEC section 4.4). A
/// count alone can't promise that: two requests each taking away one of the last two admins would
/// both see the other one and both go through. So letting someone leave also changes the role's
/// HoldersVersion, in the handler's save, and the second of two such saves finds the version changed
/// and fails. The role is read before the count for this to hold: whatever a count misses because it
/// came too early, the version read before it still catches at the save.
/// </summary>
public sealed class LastAdminGuard(IAppDbContext db)
{
    public async Task<Result> AllowLeaving(User user, CancellationToken ct)
    {
        var role = await db.Roles.SingleAsync(existing => existing.Id == user.RoleId, ct);
        if (!role.IsSystem || !user.IsActive)
        {
            return Result.Success();
        }

        var othersRemain = await db.Users.AnyAsync(
            other => other.RoleId == role.Id && other.IsActive && other.Id != user.Id,
            ct);
        if (!othersRemain)
        {
            return UserErrors.LastAdmin;
        }

        role.RecordHolderLeaving();
        return Result.Success();
    }
}
