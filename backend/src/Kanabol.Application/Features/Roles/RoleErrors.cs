using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Roles;

public static class RoleErrors
{
    public static readonly Error NotFound = Error.NotFound("Role.NotFound", "This role doesn't exist.");

    public static readonly Error NameTaken = Error.Conflict("Role.NameTaken", "A role with this name already exists.");

    public static readonly Error SystemRoleIsFixed = Error.Conflict("Role.SystemRoleIsFixed", "The system role can't be changed or deleted.");

    public static readonly Error InUse = Error.Conflict("Role.InUse", "Users still have this role, including deactivated ones.");

    public static readonly Error ChangedMeanwhile = Error.Conflict("Role.ChangedMeanwhile", "This role changed at the same moment. Try again.");

    public static readonly Error PermissionsBeyondYours = Error.Forbidden("Role.PermissionsBeyondYours", "You can't grant a permission you don't have.");
}
