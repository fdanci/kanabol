using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Users;

/// <summary>Counting failures and locking run in the database, so their tests are the handlers' tests.</summary>
public sealed class UserTests
{
    [Fact]
    public void Constructor_WhenGivenAnEmail_KeepsItTrimmedAndInLowercase() =>
        new UserBuilder().WithEmail("  Ana.Pop@Example.COM ").Build().Email.Should().Be("ana.pop@example.com");

    [Fact]
    public void Lockout_IsWhatTheSpecAsks()
    {
        User.MaxFailedLogins.Should().Be(10);
        User.LockoutDuration.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void IsLockedAt_WhenNeverLocked_ReturnsFalse() =>
        new UserBuilder().Build().IsLockedAt(DateTimeOffset.UtcNow).Should().BeFalse();

    [Fact]
    public void ChangePassword_WhenCalled_ReplacesTheHashAndClearsTheFlag()
    {
        var user = new UserBuilder().MustChangePassword().Build();

        user.ChangePassword("new-hash");

        user.PasswordHash.Should().Be("new-hash");
        user.MustChangePassword.Should().BeFalse();
    }
}
