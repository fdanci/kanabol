using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.DeactivateUser;

/// <summary>Users are never deleted: they stay referenced from history, comments and reports (SPEC section 4.4).</summary>
public sealed class DeactivateUserCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<DeactivateUserCommandHandler> logger) : ICommandHandler<DeactivateUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(DeactivateUserCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.UsersManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        var target = await db.Users.FirstOrDefaultAsync(user => user.Id == command.Id, ct);
        if (target is null)
        {
            return UserErrors.NotFound;
        }

        if (!held.Value.Covers(await db.PermissionNamesOfRole(target.RoleId).ToListAsync(ct)))
        {
            return UserErrors.PermissionsBeyondYours;
        }

        if (target.IsActive)
        {
            target.Deactivate();
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User {TargetUserId} deactivated", target.Id);
        }

        return await db.Users.AsNoTracking().Where(user => user.Id == target.Id).ToDtos().SingleAsync(ct);
    }
}
