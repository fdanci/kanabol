using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth.Commands.LoginUser;

public sealed class LoginUserCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    IClock clock,
    ILogger<LoginUserCommandHandler> logger) : ICommandHandler<LoginUserCommand, AuthSession>
{
    private static string? _decoyHash;

    public async Task<Result<AuthSession>> Handle(LoginUserCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var email = User.NormalizeEmail(command.Email);
        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Email == email, ct);

        if (user is null || !user.IsActive || user.IsLockedAt(now))
        {
            SpendTheTimeOfAVerification(command.Password);
            logger.LogWarning("Login refused for {Email}: no such active user, or locked", email);
            return AuthErrors.InvalidCredentials;
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            await db.Users.CountFailedLogin(user.Id, now, ct);
            logger.LogWarning("Login refused for {Email}: wrong password", email);
            return AuthErrors.InvalidCredentials;
        }

        user.RecordSuccessfulLogin();
        var session = await sessions.Issue(user, Guid.CreateVersion7(), ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} signed in", user.Id);
        return session;
    }

    /// <summary>
    /// Refusals that don't compare a password would come back faster than the ones that do, and the
    /// response time would tell which emails have an account.
    /// </summary>
    private void SpendTheTimeOfAVerification(string password)
    {
        _decoyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString());
        passwordHasher.Verify(_decoyHash, password);
    }
}
