using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Roles.Commands.UpdateRole;

/// <summary>
/// The people with this role get the change with their next token: only moving someone to another
/// role ends their sessions (SPEC section 4.4).
/// </summary>
public sealed class UpdateRoleCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<UpdateRoleCommandHandler> logger) : ICommandHandler<UpdateRoleCommand, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(UpdateRoleCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var role = await db.Roles
            .Include(existing => existing.RolePermissions)
            .FirstOrDefaultAsync(existing => existing.Id == command.Id, ct);
        if (role is null)
        {
            return RoleErrors.NotFound;
        }

        if (role.IsSystem)
        {
            return RoleErrors.SystemRoleIsFixed;
        }

        // What it has and what it would get: a role above the caller is out of their reach, like its holders.
        var current = await db.PermissionNamesOfRole(role.Id).ToListAsync(ct);
        if (!held.Value.Covers(current.Union(command.Permissions)))
        {
            return RoleErrors.PermissionsBeyondYours;
        }

        if (await db.IsNameTaken(command.Name, role.Id, ct))
        {
            return RoleErrors.NameTaken;
        }

        var permissionIds = await db.Permissions
            .Where(permission => command.Permissions.Contains(permission.Name))
            .Select(permission => permission.Id)
            .ToListAsync(ct);
        role.Rename(command.Name);
        role.SetPermissions(permissionIds);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The role, or one of its permissions, was removed by another request after it was read here.
            return await db.Roles.AnyAsync(existing => existing.Id == role.Id, ct) ? RoleErrors.ChangedMeanwhile : RoleErrors.NotFound;
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            // Either the name, or a permission another request granted this role after it was read here.
            return await db.IsNameTaken(command.Name, role.Id, ct) ? RoleErrors.NameTaken : RoleErrors.ChangedMeanwhile;
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            return RoleErrors.NotFound;
        }

        logger.LogInformation("Role {RoleId} now has {Permissions}", role.Id, command.Permissions);

        // With nothing to change, the save sends nothing, so this is where a role deleted meanwhile shows.
        var updated = await db.Roles.AsNoTracking().Where(existing => existing.Id == role.Id).ToDtos(db).FirstOrDefaultAsync(ct);
        return updated is null ? RoleErrors.NotFound : updated;
    }
}
