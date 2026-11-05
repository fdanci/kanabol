using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Roles.Commands.DeleteRole;

public sealed class DeleteRoleCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<DeleteRoleCommandHandler> logger) : ICommandHandler<DeleteRoleCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteRoleCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var role = await db.Roles.FirstOrDefaultAsync(existing => existing.Id == command.Id, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleErrors.SystemRoleIsFixed;
        }

        if (!held.Value.Covers(await db.PermissionNamesOfRole(role.Id).ToListAsync(ct)))
        {
            return RoleErrors.PermissionsBeyondYours;
        }

        // Deactivated users count: they stay in the database, and each of them needs a role.
        if (await db.Users.AnyAsync(user => user.RoleId == role.Id, ct))
        {
            return RoleErrors.InUse;
        }

        db.Roles.Remove(role);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            return RoleErrors.InUse;
        }
        catch (DbUpdateConcurrencyException)
        {
            return RoleErrors.NotFound;
        }

        logger.LogInformation("Role {RoleId} deleted", role.Id);
        return Unit.Value;
    }
}
