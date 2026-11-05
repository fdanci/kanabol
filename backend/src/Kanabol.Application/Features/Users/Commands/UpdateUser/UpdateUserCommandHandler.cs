using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(
    IAppDbContext db,
    CurrentPermissions currentPermissions,
    ILogger<UpdateUserCommandHandler> logger) : ICommandHandler<UpdateUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(UpdateUserCommand command, CancellationToken ct)
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

        // The email is what someone signs in with, so changing it could lock them out.
        if (!held.Value.Covers(await db.PermissionNamesOfRole(target.RoleId).ToListAsync(ct)))
        {
            return UserErrors.PermissionsBeyondYours;
        }

        var email = User.NormalizeEmail(command.Email);
        if (await db.Users.AnyAsync(user => user.Email == email && user.Id != target.Id, ct))
        {
            return UserErrors.EmailTaken;
        }

        target.UpdateProfile(email, command.DisplayName);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return UserErrors.EmailTaken;
        }

        logger.LogInformation("User {TargetUserId} updated", target.Id);

        return await db.Users.AsNoTracking().Where(user => user.Id == target.Id).ToDtos().SingleAsync(ct);
    }
}
