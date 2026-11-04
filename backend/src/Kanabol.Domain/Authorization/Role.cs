using Kanabol.Domain.Common;

namespace Kanabol.Domain.Authorization;

public sealed class Role : AuditableEntity
{
    public const string AdminName = "Admin";

    private readonly List<RolePermission> _rolePermissions = [];

    public Role(string name, bool isSystem)
    {
        Name = name;
        IsSystem = isSystem;
    }

    private Role()
    {
    }

    public string Name { get; private set; } = null!;

    public bool IsSystem { get; private set; }

    public IReadOnlyCollection<RolePermission> RolePermissions => _rolePermissions;

    public void Grant(Guid permissionId)
    {
        if (_rolePermissions.All(granted => granted.PermissionId != permissionId))
        {
            _rolePermissions.Add(new RolePermission(Id, permissionId));
        }
    }
}
