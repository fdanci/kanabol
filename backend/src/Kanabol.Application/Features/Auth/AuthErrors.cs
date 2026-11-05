using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Auth;

public static class AuthErrors
{
    /// <summary>The only answer to a refused login: it never says whether the account exists or why.</summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("Auth.InvalidCredentials", "The email or password is incorrect.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("Auth.InvalidRefreshToken", "The session has ended. Sign in again.");
}
