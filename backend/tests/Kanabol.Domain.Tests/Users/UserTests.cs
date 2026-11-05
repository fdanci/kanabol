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
    public void Constructor_WhenTheNameHasSpacesAround_KeepsItTrimmed() =>
        new User("ana@example.com", "  Ana Pop ", "hash", Guid.CreateVersion7()).DisplayName.Should().Be("Ana Pop");

    [Fact]
    public void UpdateProfile_WhenGivenValues_KeepsThemInTheirOneForm()
    {
        var user = new UserBuilder().Build();

        user.UpdateProfile("  Noua@Example.COM ", " Ana Noua ");

        user.Email.Should().Be("noua@example.com");
        user.DisplayName.Should().Be("Ana Noua");
    }

    [Fact]
    public void Deactivate_WhenCalled_LeavesTheRestAlone()
    {
        var user = new UserBuilder().Build();
        var (email, roleId) = (user.Email, user.RoleId);

        user.Deactivate();

        user.IsActive.Should().BeFalse();
        user.Email.Should().Be(email);
        user.RoleId.Should().Be(roleId);
    }

    [Fact]
    public void Activate_AfterDeactivate_LeavesTheRestAlone()
    {
        var user = new UserBuilder().MustChangePassword().Build();
        var (email, roleId, hash) = (user.Email, user.RoleId, user.PasswordHash);
        user.Deactivate();

        user.Activate();

        user.IsActive.Should().BeTrue();
        user.Email.Should().Be(email);
        user.RoleId.Should().Be(roleId);
        user.PasswordHash.Should().Be(hash);
        user.MustChangePassword.Should().BeTrue();
    }

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
