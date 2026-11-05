using Kanabol.Domain.Authorization;
using Kanabol.Domain.Common;

namespace Kanabol.Domain.Users;

public sealed class User : AuditableEntity
{
    public const int MaxFailedLogins = 10;
    public const int MaxEmailLength = 320;
    public const int MaxDisplayNameLength = 200;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public User(string email, string displayName, string passwordHash, Guid roleId, bool mustChangePassword = false)
    {
        Email = NormalizeEmail(email);
        DisplayName = displayName.Trim();
        PasswordHash = passwordHash;
        RoleId = roleId;
        MustChangePassword = mustChangePassword;
        IsActive = true;
    }

    private User()
    {
    }

    public string Email { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public Guid RoleId { get; private set; }

    public Role Role { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public int FailedLoginAttempts { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool MustChangePassword { get; private set; }

    /// <summary>An email has one form here, so Ana@x.ro and ana@x.ro can't be two accounts.</summary>
#pragma warning disable CA1308 // Lowercase on purpose: it's stored and shown, not only compared.
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
#pragma warning restore CA1308

    public bool IsLockedAt(DateTimeOffset now) => LockedUntil > now;

    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }

    public void UpdateProfile(string email, string displayName)
    {
        Email = NormalizeEmail(email);
        DisplayName = displayName.Trim();
    }

    public void ChangeRole(Guid roleId) => RoleId = roleId;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        MustChangePassword = false;
        RecordSuccessfulLogin();
    }
}
