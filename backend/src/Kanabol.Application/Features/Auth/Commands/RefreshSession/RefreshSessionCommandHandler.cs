using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Auth.Commands.RefreshSession;

public sealed class RefreshSessionCommandHandler(
    IAppDbContext db,
    ITokenService tokens,
    SessionIssuer sessions,
    IClock clock,
    ILogger<RefreshSessionCommandHandler> logger) : ICommandHandler<RefreshSessionCommand, AuthSession>
{
    public async Task<Result<AuthSession>> Handle(RefreshSessionCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokens.HashRefreshToken(command.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, ct);

        if (token is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        if (token.IsRevoked)
        {
            return await RefuseReuse(token, now, ct);
        }

        if (token.IsExpiredAt(now))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == token.UserId && candidate.IsActive, ct);
        if (user is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        token.Revoke(now);
        var session = await sessions.Issue(user, token.FamilyId, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this same token after it was read here, so it's been used twice.
            return await RefuseReuse(token, now, ct);
        }

        return session;
    }

    private async Task<Result<AuthSession>> RefuseReuse(RefreshToken token, DateTimeOffset now, CancellationToken ct)
    {
        await db.RefreshTokens.RevokeFamily(token.FamilyId, now, ct);

        logger.LogWarning(
            "A used refresh token of user {UserId} came back, so its family {FamilyId} is revoked",
            token.UserId,
            token.FamilyId);

        return AuthErrors.InvalidRefreshToken;
    }
}
