using Kanabol.Application.Abstractions;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Auth;

/// <summary>Creates a user's tokens. The permissions come from the database, so each new token has the current ones.</summary>
public sealed class SessionIssuer(IAppDbContext db, ITokenService tokens, IClock clock)
{
    /// <summary>Adds the refresh token to the context. Saving it belongs to the handler, in its one transaction.</summary>
    public async Task<AuthSession> Issue(User user, Guid familyId, CancellationToken ct)
    {
        var permissions = await db.Roles
            .Where(role => role.Id == user.RoleId)
            .SelectMany(role => role.RolePermissions)
            .Join(db.Permissions, granted => granted.PermissionId, permission => permission.Id, (_, permission) => permission.Name)
            .ToListAsync(ct);

        var refreshToken = tokens.CreateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken(user.Id, familyId, refreshToken.Hash, refreshToken.ExpiresAt, clock.UtcNow));

        var accessToken = tokens.CreateAccessToken(user.Id, permissions, user.MustChangePassword);
        return new AuthSession(accessToken, refreshToken, user.MustChangePassword);
    }
}
