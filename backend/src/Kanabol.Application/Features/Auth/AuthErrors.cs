using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Auth;

public static class AuthErrors
{
    /// <summary>The only answer to a refused login: it never says whether the account exists or why.</summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "The email or password is incorrect.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.InvalidRefreshToken", "The session has ended. Sign in again.");

    public static readonly Error SessionEnded =
        Error.Unauthorized("Auth.SessionEnded", "The session has ended. Sign in again.");

    public static readonly Error PasswordChangeRequired =
        Error.Forbidden("Auth.PasswordChangeRequired", "The password has to be changed first.");

    public static readonly Error PermissionRequired =
        Error.Forbidden("Auth.PermissionRequired", "You don't have permission to do this.");

    // The API answers it with a 429, which the error types do not have, since only this limit needs one.
    public static readonly Error TooManyLoginAttempts =
        Error.Failure("Auth.TooManyLoginAttempts", "Too many login attempts. Wait a minute and try again.");

    public static readonly Error SessionChangedMeanwhile =
        Error.Conflict("Auth.SessionChangedMeanwhile", "A session changed at the same moment. Try again.");

    // On the field rather than a 401, which the frontend answers by refreshing the session.
    public static readonly Error WrongCurrentPassword = Error.Validation(new Dictionary<string, string[]>
    {
        ["CurrentPassword"] = ["The current password is incorrect."],
    });

    public static readonly Error NewPasswordSameAsCurrent = Error.Validation(new Dictionary<string, string[]>
    {
        ["NewPassword"] = ["The new password has to differ from the current one."],
    });
}
