using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Roles;
using Kanabol.Application.Features.Users;

namespace Kanabol.Application.Tests.Features;

/// <summary>
/// The tests compare results with these same objects, so a wrong type here would pass them all and
/// turn a 409 into a 500. Written out, as the frontend and backend/CLAUDE.md know them.
/// </summary>
public sealed class ManagementErrorsTests
{
    public static TheoryData<Error, string, ErrorType> Errors => new()
    {
        { RoleErrors.NotFound, "Role.NotFound", ErrorType.NotFound },
        { RoleErrors.NameTaken, "Role.NameTaken", ErrorType.Conflict },
        { RoleErrors.SystemRoleIsFixed, "Role.SystemRoleIsFixed", ErrorType.Conflict },
        { RoleErrors.InUse, "Role.InUse", ErrorType.Conflict },
        { RoleErrors.ChangedMeanwhile, "Role.ChangedMeanwhile", ErrorType.Conflict },
        { RoleErrors.PermissionsBeyondYours, "Role.PermissionsBeyondYours", ErrorType.Forbidden },
        { UserErrors.NotFound, "User.NotFound", ErrorType.NotFound },
        { UserErrors.EmailTaken, "User.EmailTaken", ErrorType.Conflict },
        { UserErrors.LastAdmin, "User.LastAdmin", ErrorType.Conflict },
        { UserErrors.ChangedMeanwhile, "User.ChangedMeanwhile", ErrorType.Conflict },
        { UserErrors.RoleBeyondYours, "User.RoleBeyondYours", ErrorType.Forbidden },
        { UserErrors.PermissionsBeyondYours, "User.PermissionsBeyondYours", ErrorType.Forbidden },
        { UserErrors.OwnPasswordReset, "User.OwnPasswordReset", ErrorType.Forbidden },
        { UserErrors.RoleNotFound, "Validation", ErrorType.Validation },
        { AuthErrors.PermissionRequired, "Auth.PermissionRequired", ErrorType.Forbidden },
        { AuthErrors.SessionEnded, "Auth.SessionEnded", ErrorType.Unauthorized },
        { AuthErrors.PasswordChangeRequired, "Auth.PasswordChangeRequired", ErrorType.Forbidden },
        { AuthErrors.SessionChangedMeanwhile, "Auth.SessionChangedMeanwhile", ErrorType.Conflict },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Error_WhenRead_HasItsCodeAndType(Error error, string code, ErrorType type)
    {
        error.Code.Should().Be(code);
        error.Type.Should().Be(type);
    }
}
