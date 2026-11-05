using Kanabol.Domain.Common;

namespace Kanabol.Domain.Users;

public sealed class RefreshToken : Entity
{
    public RefreshToken(Guid userId, Guid familyId, string tokenHash, DateTimeOffset expiresAt, DateTimeOffset createdAt)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpiredAt(DateTimeOffset now) => ExpiresAt <= now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
