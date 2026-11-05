using Kanabol.Domain.Users;

namespace Kanabol.Domain.Tests.Users;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly RefreshToken _token = new(Guid.CreateVersion7(), Guid.CreateVersion7(), "hash", _now.AddDays(7), _now);

    [Fact]
    public void IsRevoked_WhenNew_ReturnsFalse() => _token.IsRevoked.Should().BeFalse();

    [Fact]
    public void Revoke_WhenFirstCalled_RemembersWhen()
    {
        _token.Revoke(_now);

        _token.IsRevoked.Should().BeTrue();
        _token.RevokedAt.Should().Be(_now);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_KeepsTheFirstTime()
    {
        _token.Revoke(_now);

        _token.Revoke(_now.AddDays(1));

        _token.RevokedAt.Should().Be(_now);
    }

    [Fact]
    public void IsExpiredAt_UpToItsExpiry_ReturnsFalseAndThenTrue()
    {
        _token.IsExpiredAt(_now.AddDays(7).AddTicks(-1)).Should().BeFalse();
        _token.IsExpiredAt(_now.AddDays(7)).Should().BeTrue();
    }
}
