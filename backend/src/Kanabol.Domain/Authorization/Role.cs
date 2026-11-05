using Kanabol.Domain.Common;

namespace Kanabol.Domain.Authorization;

public sealed class Role : AuditableEntity
{
    public const string AdminName = "Admin";
    public const int MaxNameLength = 100;

    private readonly List<RolePermission> _rolePermissions = [];

    public Role(string name, bool isSystem)
    {
        Name = name.Trim();
        IsSystem = isSystem;
    }

    private Role()
    {
    }

    public string Name { get; private set; } = null!;

    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions;

    public void Rename(string name)
    {
        EnsureNotSystem();
        Name = name.Trim();
    }

    public void SetPermissions(IReadOnlyCollection<Guid> permissionIds)
    {
        EnsureNotSystem();

        _rolePermissions.RemoveAll(granted => !permissionIds.Contains(granted.PermissionId));
        foreach (var permissionId in permissionIds)
        {
            Grant(permissionId);
        }
    }

    /// <summary>Not guarded: the seed gives the system role each permission that's new in the code.</summary>
    public void Grant(Guid permissionId)
    {
        if (_rolePermissions.All(granted => granted.PermissionId != permissionId))
        {
            _rolePermissions.Add(new RolePermission(Id, permissionId));
        }
    }

    /// <summary>Handlers refuse this with an error first, so getting here means one of them forgot.</summary>
    private void EnsureNotSystem()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("The system role can't be changed.");
        }
    }
}
