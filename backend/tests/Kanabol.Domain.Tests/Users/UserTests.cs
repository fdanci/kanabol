using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Users;

public sealed class UserTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WhenGivenAnEmail_KeepsItTrimmedAndInLowercase() =>
        new UserBuilder().WithEmail("  Ana.Pop@Example.COM ").Build().Email.Should().Be("ana.pop@example.com");

    [Fact]
    public void RecordFailedLogin_BeforeTheLimit_CountsWithoutLocking()
    {
        var user = new UserBuilder().Build();

        FailLogins(user, User.MaxFailedLogins - 1);

        user.FailedLoginAttempts.Should().Be(User.MaxFailedLogins - 1);
        user.LockedUntil.Should().BeNull();
        user.IsLockedAt(_now).Should().BeFalse();
    }

    [Fact]
    public void RecordFailedLogin_AtTheLimit_Locks15MinutesAndStartsCountingAgain()
    {
        var user = new UserBuilder().Build();

        FailLogins(user, User.MaxFailedLogins);

        User.MaxFailedLogins.Should().Be(10);
        user.LockedUntil.Should().Be(_now.AddMinutes(15));
        user.FailedLoginAttempts.Should().Be(0);
    }

    [Fact]
    public void IsLockedAt_UntilTheLockExpires_ReturnsTrue()
    {
        var user = new UserBuilder().Build();
        FailLogins(user, User.MaxFailedLogins);

        user.IsLockedAt(_now.AddMinutes(15).AddTicks(-1)).Should().BeTrue();
        user.IsLockedAt(_now.AddMinutes(15)).Should().BeFalse();
    }

    [Fact]
    public void RecordSuccessfulLogin_AfterFailures_ClearsThemAndTheLock()
    {
        var user = new UserBuilder().Build();
        FailLogins(user, User.MaxFailedLogins + 3);

        user.RecordSuccessfulLogin();

        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void ChangePassword_WhenCalled_ReplacesTheHashAndClearsTheFlagAndFailures()
    {
        var user = new UserBuilder().MustChangePassword().Build();
        FailLogins(user, User.MaxFailedLogins + 3);

        user.ChangePassword("new-hash");

        user.PasswordHash.Should().Be("new-hash");
        user.MustChangePassword.Should().BeFalse();
        user.FailedLoginAttempts.Should().Be(0);
        user.LockedUntil.Should().BeNull();
    }

    private static void FailLogins(User user, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            user.RecordFailedLogin(_now);
        }
    }
}
