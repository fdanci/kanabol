using Kanabol.Domain.Authorization;

namespace Kanabol.Domain.Tests.Authorization;

public sealed class PermissionsTests
{
    // Written out rather than taken from the constants: the seed syncs the database with this list.
    [Fact]
    public void All_WhenRead_IsTheListInTheSpec() =>
        Permissions.All.Should().BeEquivalentTo(
            "users.manage",
            "roles.manage",
            "projects.create",
            "projects.view_all",
            "projects.manage",
            "tasks.archive",
            "reports.view");

    [Fact]
    public void All_WhenRead_HasNoDuplicates() =>
        Permissions.All.Should().OnlyHaveUniqueItems();
}
