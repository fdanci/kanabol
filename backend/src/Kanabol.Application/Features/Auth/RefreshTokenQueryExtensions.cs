using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Auth;

public static class RefreshTokenQueryExtensions
{
    /// <summary>Runs right away, outside SaveChanges, and only touches the tokens that are still live.</summary>
    public static Task<int> RevokeFamily(this IQueryable<RefreshToken> tokens, Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        tokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, now), ct);
}
