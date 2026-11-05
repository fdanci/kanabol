using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Roles.Commands.CreateRole;

public sealed class CreateRoleCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<CreateRoleCommandHandler> logger) : ICommandHandler<CreateRoleCommand, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(CreateRoleCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.RolesManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        if (!held.Value.Covers(command.Permissions))
        {
            return RoleErrors.PermissionsBeyondYours;
        }

        if (await db.IsNameTaken(command.Name, exceptRoleId: null, ct))
        {
            return RoleErrors.NameTaken;
        }

        var role = new Role(command.Name, isSystem: false);
        var permissionIds = await db.Permissions
            .Where(permission => command.Permissions.Contains(permission.Name))
            .Select(permission => permission.Id)
            .ToListAsync(ct);
        role.SetPermissions(permissionIds);
        db.Roles.Add(role);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return RoleErrors.NameTaken;
        }

        logger.LogInformation("Role {RoleId} created with {Permissions}", role.Id, command.Permissions);

        return await db.Roles.AsNoTracking().Where(existing => existing.Id == role.Id).ToDtos(db).SingleAsync(ct);
    }
}
