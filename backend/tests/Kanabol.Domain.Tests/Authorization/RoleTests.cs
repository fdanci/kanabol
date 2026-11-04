using Kanabol.Domain.Authorization;

namespace Kanabol.Domain.Tests.Authorization;

public sealed class RoleTests
{
    [Fact]
    public void Grant_WhenThePermissionIsNew_AddsIt()
    {
        var role = new Role("Manager", isSystem: false);
        var permissionId = Guid.CreateVersion7();

        role.Grant(permissionId);

        role.RolePermissions.Should().ContainSingle(granted => granted.PermissionId == permissionId && granted.RoleId == role.Id);
    }

    [Fact]
    public void Grant_WhenAlreadyGranted_KeepsASingleEntry()
    {
        var role = new Role("Manager", isSystem: false);
        var permissionId = Guid.CreateVersion7();
        role.Grant(permissionId);

        role.Grant(permissionId);

        role.RolePermissions.Should().ContainSingle();
    }
}
