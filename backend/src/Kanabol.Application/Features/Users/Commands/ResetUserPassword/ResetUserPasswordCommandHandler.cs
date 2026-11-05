using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.ResetUserPassword;

/// <summary>
/// An admin gives someone a new initial password, which they have to change at their next login. Their
/// sessions end in the same save, as when their role changes. Nobody resets their own here: that goes
/// through change-password, which asks for the current one, so a stolen access token can't take over
/// the account it belongs to.
/// </summary>
public sealed class ResetUserPasswordCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    CurrentPermissions currentPermissions,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ResetUserPasswordCommandHandler> logger) : ICommandHandler<ResetUserPasswordCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(ResetUserPasswordCommand command, CancellationToken ct)
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

        if (target.Id == currentUser.Id)
        {
            return UserErrors.OwnPasswordReset;
        }

        if (!held.Value.Covers(await db.PermissionNamesOfRole(target.RoleId).ToListAsync(ct)))
        {
            return UserErrors.PermissionsBeyondYours;
        }

        var now = clock.UtcNow;
        var liveTokens = await db.RefreshTokens.Where(token => token.UserId == target.Id && token.RevokedAt == null).ToListAsync(ct);

        target.ResetPassword(passwordHasher.Hash(command.InitialPassword));
        liveTokens.ForEach(token => token.Revoke(now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return UserErrors.ChangedMeanwhile;
        }

        logger.LogInformation("User {TargetUserId} had their password reset, which ended {SessionCount} tokens", target.Id, liveTokens.Count);

        return await db.Users.AsNoTracking().Where(user => user.Id == target.Id).ToDtos().SingleAsync(ct);
    }
}
