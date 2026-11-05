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

    [Fact]
    public void Constructor_WhenTheNameHasSpacesAround_KeepsItTrimmed() =>
        new Role("  Manager ", isSystem: false).Name.Should().Be("Manager");

    [Fact]
    public void Rename_OnAnOrdinaryRole_ChangesTheName()
    {
        var role = new Role("Manager", isSystem: false);

        role.Rename(" Sef ");

        role.Name.Should().Be("Sef");
    }

    [Fact]
    public void Rename_OnTheSystemRole_Throws()
    {
        var role = new Role(Role.AdminName, isSystem: true);

        var rename = () => role.Rename("Sef");

        rename.Should().Throw<InvalidOperationException>();
        role.Name.Should().Be(Role.AdminName);
    }

    [Fact]
    public void SetPermissions_OnAnOrdinaryRole_ReplacesThem()
    {
        var role = new Role("Manager", isSystem: false);
        var kept = Guid.CreateVersion7();
        var removed = Guid.CreateVersion7();
        var added = Guid.CreateVersion7();
        role.Grant(kept);
        role.Grant(removed);

        role.SetPermissions([kept, added]);

        role.RolePermissions.Select(granted => granted.PermissionId).Should().BeEquivalentTo([kept, added]);
    }

    [Fact]
    public void SetPermissions_OnTheSystemRole_ThrowsAndKeepsThem()
    {
        var role = new Role(Role.AdminName, isSystem: true);
        var permissionId = Guid.CreateVersion7();
        role.Grant(permissionId);

        var restrict = () => role.SetPermissions([]);

        restrict.Should().Throw<InvalidOperationException>();
        role.RolePermissions.Should().ContainSingle();
    }

    // The seed gives Admin each permission that's new in the code.
    [Fact]
    public void Grant_OnTheSystemRole_StillWorks()
    {
        var role = new Role(Role.AdminName, isSystem: true);

        role.Grant(Guid.CreateVersion7());

        role.RolePermissions.Should().ContainSingle();
    }
}
