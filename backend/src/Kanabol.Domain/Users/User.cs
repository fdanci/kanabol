using Kanabol.Domain.Authorization;
using Kanabol.Domain.Common;

namespace Kanabol.Domain.Users;

public sealed class User : AuditableEntity
{
    public User(string email, string displayName, string passwordHash, Guid roleId, bool mustChangePassword = false)
    {
        Email = email;
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

    public DateTimeOffset? LockedUntil { get; private set; }

    public bool MustChangePassword { get; private set; }
}
