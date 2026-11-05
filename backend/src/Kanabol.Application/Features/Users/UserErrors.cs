using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Users;

public static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("User.NotFound", "This user doesn't exist.");

    public static readonly Error EmailTaken = Error.Conflict("User.EmailTaken", "A user with this email already exists.");

    public static readonly Error ChangedMeanwhile = Error.Conflict("User.ChangedMeanwhile", "This user changed at the same moment. Try again.");

    public static readonly Error RoleBeyondYours = Error.Forbidden("User.RoleBeyondYours", "You can't give someone a role with permissions you don't have.");

    public static readonly Error PermissionsBeyondYours = Error.Forbidden("User.PermissionsBeyondYours", "You can't change someone who has permissions you don't have.");

    public static readonly Error RoleNotFound = Error.Validation(new Dictionary<string, string[]>
    {
        ["RoleId"] = ["This role doesn't exist."],
    });
}
