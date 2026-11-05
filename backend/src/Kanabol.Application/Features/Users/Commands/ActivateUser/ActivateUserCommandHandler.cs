using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.ActivateUser;

/// <summary>
/// Brings back someone who was deactivated, with the role and password they had. It starts no session:
/// those ended with the deactivation, and the user signs in again.
/// </summary>
public sealed class ActivateUserCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<ActivateUserCommandHandler> logger) : ICommandHandler<ActivateUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(ActivateUserCommand command, CancellationToken ct)
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

        if (!target.IsActive)
        {
            target.Activate();

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return UserErrors.ChangedMeanwhile;
            }

            logger.LogInformation("User {TargetUserId} reactivated", target.Id);
        }

        return await db.Users.AsNoTracking().Where(user => user.Id == target.Id).ToDtos().SingleAsync(ct);
    }
}
