using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.ChangeUserRole;

/// <summary>
/// Ends the user's sessions in the same save, so a refresh running at the same moment either happened
/// before, and its token gets revoked here too, or makes this save fail and nothing changes. Then their
/// connections to the board hub close, and come back with a token of the new role (SPEC section 9).
/// </summary>
public sealed class ChangeUserRoleCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    LastAdminGuard lastAdmin,
    IClock clock,
    IRealtimeAccess realtimeAccess,
    ILogger<ChangeUserRoleCommandHandler> logger) : ICommandHandler<ChangeUserRoleCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(ChangeUserRoleCommand command, CancellationToken ct)
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

        if (!await db.Roles.AnyAsync(role => role.Id == command.RoleId, ct))
        {
            return UserErrors.RoleNotFound;
        }

        if (!held.Value.Covers(await db.PermissionNamesOfRole(command.RoleId).ToListAsync(ct)))
        {
            return UserErrors.RoleBeyondYours;
        }

        if (target.RoleId != command.RoleId)
        {
            var failure = await MoveToRole(target, command.RoleId, ct);
            if (failure is not null)
            {
                return failure;
            }
        }

        return await db.Users.AsNoTracking().Where(user => user.Id == target.Id).ToDtos().SingleAsync(ct);
    }

    private async Task<Error?> MoveToRole(User target, Guid roleId, CancellationToken ct)
    {
        var leaving = await lastAdmin.AllowLeaving(target, ct);
        if (leaving.IsFailure)
        {
            return leaving.Error;
        }

        var now = clock.UtcNow;
        var liveTokens = await db.RefreshTokens.Where(token => token.UserId == target.Id && token.RevokedAt == null).ToListAsync(ct);

        target.ChangeRole(roleId);
        liveTokens.ForEach(token => token.Revoke(now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UserErrors.ChangedMeanwhile;
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            return UserErrors.RoleNotFound;
        }

        logger.LogInformation("User {TargetUserId} moved to role {RoleId}, which ended {SessionCount} tokens", target.Id, roleId, liveTokens.Count);
        await realtimeAccess.DisconnectAsync(target.Id, CancellationToken.None);
        return null;
    }
}
