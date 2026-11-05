using Kanabol.Domain.Authorization;
using Kanabol.Domain.Common;

namespace Kanabol.Domain.Users;

public sealed class User : AuditableEntity
{
    public const int MaxFailedLogins = 10;

    private static readonly TimeSpan _lockoutDuration = TimeSpan.FromMinutes(15);

    public User(string email, string displayName, string passwordHash, Guid roleId, bool mustChangePassword = false)
    {
        Email = NormalizeEmail(email);
        DisplayName = displayName;
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

    public void RecordFailedLogin(DateTimeOffset now)
    {
        FailedLoginAttempts++;

        if (FailedLoginAttempts >= MaxFailedLogins)
        {
            LockedUntil = now + _lockoutDuration;
            FailedLoginAttempts = 0;
        }
    }

    public void ChangePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        MustChangePassword = false;
        RecordSuccessfulLogin();
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntil = null;
    }
}
