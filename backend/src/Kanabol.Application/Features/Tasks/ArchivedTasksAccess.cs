using Kanabol.Application.Abstractions;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Domain.Authorization;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// Archived tasks come only with includeArchived=true, and only to whoever holds tasks.archive
/// (SPEC section 5.1). It reads the token, as every endpoint outside user and role management does,
/// and the permission's name goes only into the log (SPEC section 4.4).
/// </summary>
public sealed class ArchivedTasksAccess(ICurrentUser currentUser, ILogger<ArchivedTasksAccess> logger)
{
    public Result Check(bool includeArchived)
    {
        if (!includeArchived || currentUser.HasPermission(Permissions.TasksArchive))
        {
            return Result.Success();
        }

        logger.LogWarning("User {RefusedUserId} asked for archived tasks without {Permission}", currentUser.Id, Permissions.TasksArchive);
        return AuthErrors.PermissionRequired;
    }
}
