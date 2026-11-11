using Kanabol.Api.Authorization;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Authorization;

/// <summary>Reads the attributes of the API that ships, so a new action can't forget the permission of its controller.</summary>
public sealed class EndpointPermissionsTests(ApiFixture api) : ApiTest(api)
{
    private IEnumerable<Endpoint> RealActions =>
        Api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Assembly == typeof(Program).Assembly);

    [Theory]
    [InlineData("Roles", Permissions.RolesManage, 5)]
    [InlineData("Permissions", Permissions.RolesManage, 1)]
    [InlineData("Users", Permissions.UsersManage, 9)]
    public void Controller_OnEveryAction_NeedsItsPermission(string controller, string permission, int actions)
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == controller)
            .ToList();

        found.Should().HaveCount(actions);
        found.Should().OnlyContain(endpoint => endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any(required => required.Permission == permission));
    }

    // The rest of the project endpoints check the role on the project, through IProjectAccess (SPEC section 4.4).
    [Fact]
    public void Projects_OnlyCreatingOne_NeedsAPermission()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Projects")
            .ToList();

        found.Should().HaveCount(12);
        found.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any())
            .Select(endpoint => (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName, endpoint.Metadata.GetMetadata<HasPermissionAttribute>()!.Permission))
            .Should().Equal(("CreateProject", Permissions.ProjectsCreate));
    }

    // The rest of the task endpoints, the history and the column "Arhivate" among them, check access to the
    // project in the handler (SPEC sections 4.4 and 5.1).
    [Fact]
    public void Tasks_OnlyCreatingAndRestoringOne_NeedAPermissionOnTheEndpoint()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Tasks")
            .ToList();

        found.Should().HaveCount(17);
        found.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any())
            .Select(endpoint => (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName, endpoint.Metadata.GetMetadata<HasPermissionAttribute>()!.Permission))
            .Should().BeEquivalentTo([("CreateTask", Permissions.TasksCreate), ("RestoreTask", Permissions.TasksArchive)]);
    }

    // The columns check the role on the project in the handler, an owner to change them (UI spec section 7.4).
    [Fact]
    public void BoardColumns_NeedNoPermissionOnTheEndpoint()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "BoardColumns")
            .ToList();

        found.Should().HaveCount(6);
        found.Should().OnlyContain(endpoint => !endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any());
    }

    // Everyone signed in reads and marks only their own notifications, without a permission (SPEC section 4.4).
    [Fact]
    public void Notifications_NeedNoPermissionOnTheEndpoint()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Notifications")
            .ToList();

        found.Should().HaveCount(4);
        found.Should().OnlyContain(endpoint => !endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any());
    }

    // Everyone signed in reads their own numbers and tasks, without a permission (SPEC section 4.4).
    [Fact]
    public void Me_NeedsNoPermissionOnTheEndpoint()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Me")
            .ToList();

        found.Should().HaveCount(2);
        found.Should().OnlyContain(endpoint => !endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any());
    }

    // reports.view, or an owner of the project asked for: the handlers check it, not the endpoint (SPEC section 5.1.1).
    [Fact]
    public void Reports_NeedNoPermissionOnTheEndpoint()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Reports")
            .ToList();

        found.Should().HaveCount(4);
        found.Should().OnlyContain(endpoint => !endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any());
    }

    // Anyone signed in reads the work schedule; settings.manage changes it, checked again in the database (SPEC section 4.4).
    [Fact]
    public void Settings_OnlyChangingTheSchedule_NeedsSettingsManage()
    {
        var found = RealActions
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ControllerName == "Settings")
            .ToList();

        found.Should().HaveCount(2);
        found.Where(endpoint => endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>().Any())
            .Select(endpoint => (endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName, endpoint.Metadata.GetMetadata<HasPermissionAttribute>()!.Permission))
            .Should().BeEquivalentTo([("SetWorkSchedule", Permissions.SettingsManage)]);
    }

    [Fact]
    public void HasPermission_InTheApiThatShips_NamesOnlyPermissionsThatExist() =>
        RealActions
            .SelectMany(endpoint => endpoint.Metadata.GetOrderedMetadata<HasPermissionAttribute>())
            .Select(required => required.Permission)
            .Should().BeSubsetOf(Permissions.All);
}
