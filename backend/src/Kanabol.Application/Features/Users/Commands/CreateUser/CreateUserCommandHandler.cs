using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    CurrentPermissions currentPermissions,
    ILogger<CreateUserCommandHandler> logger) : ICommandHandler<CreateUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(CreateUserCommand command, CancellationToken ct)
    {
        var held = await currentPermissions.Require(Permissions.UsersManage, ct);
        if (held.IsFailure)
        {
            return held.Error;
        }

        if (!await db.Roles.AnyAsync(role => role.Id == command.RoleId, ct))
        {
            return UserErrors.RoleNotFound;
        }

        if (!held.Value.Covers(await db.PermissionNamesOfRole(command.RoleId).ToListAsync(ct)))
        {
            return UserErrors.RoleBeyondYours;
        }

        var email = User.NormalizeEmail(command.Email);
        if (await db.Users.AnyAsync(user => user.Email == email, ct))
        {
            return UserErrors.EmailTaken;
        }

        var created = new User(email, command.DisplayName, passwordHasher.Hash(command.InitialPassword), command.RoleId, mustChangePassword: true);
        db.Users.Add(created);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return UserErrors.EmailTaken;
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            return UserErrors.RoleNotFound;
        }

        logger.LogInformation("User {CreatedUserId} created with role {RoleId}", created.Id, command.RoleId);

        return await db.Users.AsNoTracking().Where(user => user.Id == created.Id).ToDtos().SingleAsync(ct);
    }
}
