using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth.Commands.ChangePassword;

/// <summary>
/// Ends every earlier session and starts a new one for this device, all in one save. So a refresh
/// running at the same moment either happened before, and its token gets revoked here too, or makes
/// this save fail and nothing changes.
/// </summary>
public sealed class ChangePasswordCommandHandler(
    IAppDbContext db,
    IPasswordHasher passwordHasher,
    SessionIssuer sessions,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ChangePasswordCommandHandler> logger) : ICommandHandler<ChangePasswordCommand, AuthSession>
{
    public async Task<Result<AuthSession>> Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var userId = currentUser.Id;
        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId && candidate.IsActive, ct);

        if (user is null)
        {
            return AuthErrors.SessionEnded;
        }

        if (user.IsLockedAt(now))
        {
            return AuthErrors.WrongCurrentPassword;
        }

        // A stolen access token mustn't be a way to guess the password without the limits of a login.
        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword))
        {
            user.RecordFailedLogin(now);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("User {UserId} gave a wrong current password, locked until {LockedUntil}", userId, user.LockedUntil);
            return AuthErrors.WrongCurrentPassword;
        }

        if (passwordHasher.Verify(user.PasswordHash, command.NewPassword))
        {
            return AuthErrors.NewPasswordSameAsCurrent;
        }

        var liveTokens = await db.RefreshTokens.Where(token => token.UserId == userId && token.RevokedAt == null).ToListAsync(ct);

        user.ChangePassword(passwordHasher.Hash(command.NewPassword));
        liveTokens.ForEach(token => token.Revoke(now));
        var session = await sessions.Issue(user, Guid.CreateVersion7(), ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return AuthErrors.SessionChangedMeanwhile;
        }

        logger.LogInformation("User {UserId} changed their password, which ended {SessionCount} tokens", userId, liveTokens.Count);
        return session;
    }
}
