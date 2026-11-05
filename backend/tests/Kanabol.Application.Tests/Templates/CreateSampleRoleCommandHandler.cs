using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Templates;

public sealed class CreateSampleRoleCommandHandler(IAppDbContext db, ICurrentUser currentUser)
    : ICommandHandler<CreateSampleRoleCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateSampleRoleCommand command, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.RolesManage))
        {
            return Error.Forbidden("Role.Forbidden", "You can't manage roles.");
        }

        if (await db.Roles.AnyAsync(role => role.Name == command.Name, ct))
        {
            return Error.Conflict("Role.NameTaken", "A role with this name exists.");
        }

        var created = new Role(command.Name, isSystem: false);
        db.Roles.Add(created);
        await db.SaveChangesAsync(ct);
        return created.Id;
    }
}
