using System.Net;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Microsoft.AspNetCore.SignalR.Client;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// Who stops hearing what, at once (Tasks.md group 19; SPEC section 9): someone taken out of a project leaves
/// its group on every connection, unless a permission still shows them every project; a change of role and a
/// deactivation close the person's connections, which come back with a fresh token, or not at all.
/// Everything goes through the API's endpoints, as the people who make those changes do.
/// </summary>
public sealed class RealtimeAccessTests(ApiFixture api) : RealtimeApiTest(api)
{
    private const string TaskCreated = "TaskCreated";

    private async Task CreateTaskThroughTheApi(Guid projectId, string token) =>
        (await Send(HttpMethod.Post, $"{Projects}/{projectId}/tasks", token, new { title = "După schimbare", description = "", priority = "Medium" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

    [Fact]
    public async Task RemoveMember_TakesEveryConnectionOfTheirsOutOfTheProject_AndTheOthersKeepHearing()
    {
        var (owner, ownerToken) = await SignedIn(Permissions.TasksCreate);
        var dan = await CreateUser(UnusedEmail());
        var eva = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(dan).WithMember(eva));
        var danTab = await Connect(Api.Factory.CreateToken(dan.Id), TaskCreated, ProbeEvent);
        var danOtherTab = await Connect(Api.Factory.CreateToken(dan.Id), TaskCreated, ProbeEvent);
        var evaTab = await Connect(Api.Factory.CreateToken(eva.Id), TaskCreated, ProbeEvent);
        foreach (var tab in new[] { danTab, danOtherTab, evaTab })
        {
            (await Join(tab, project.Id)).Should().BeNull();
        }

        (await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{dan.Id}", ownerToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await CreateTaskThroughTheApi(project.Id, ownerToken);

        // Once Eva heard it, the hub had written it to every connection in the group.
        (await evaTab.NextIs(TaskCreated)).GetProperty("projectId").GetGuid().Should().Be(project.Id);
        await SendToUser(dan.Id, "the end");
        (await MarkerOf(danTab)).Should().Be("the end", "the new task's event didn't reach Dan before it");
        (await MarkerOf(danOtherTab)).Should().Be("the end");
        danTab.Connection.State.Should().Be(HubConnectionState.Connected, "Dan keeps his own group, for his notifications");
    }

    [Fact]
    public async Task RemoveMember_WhoSeesEveryProject_KeepsHearingIt()
    {
        var (owner, ownerToken) = await SignedIn(Permissions.TasksCreate);
        var vera = await CreateUser(UnusedEmail(), permissions: [Permissions.ProjectsViewAll]);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(vera));
        var veraTab = await Connect(Api.Factory.CreateToken(vera.Id, Permissions.ProjectsViewAll), TaskCreated);
        (await Join(veraTab, project.Id)).Should().BeNull();

        (await Send(HttpMethod.Delete, $"{Projects}/{project.Id}/members/{vera.Id}", ownerToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await CreateTaskThroughTheApi(project.Id, ownerToken);

        (await veraTab.NextIs(TaskCreated)).GetProperty("projectId").GetGuid().Should().Be(project.Id);
    }

    [Fact]
    public async Task ChangeUserRole_ClosesEveryConnectionOfTheirs_AndNoOneElses()
    {
        var managerToken = await TokenWith(Permissions.UsersManage);
        var dan = await CreateUser(UnusedEmail());
        var eva = await CreateUser(UnusedEmail());
        var role = await CreateRoleInDatabase("Analist în timp real");
        var danTab = await Connect(Api.Factory.CreateToken(dan.Id));
        var danOtherTab = await Connect(Api.Factory.CreateToken(dan.Id));
        var evaTab = await Connect(Api.Factory.CreateToken(eva.Id), ProbeEvent);

        (await Send(HttpMethod.Put, $"/api/users/{dan.Id}/role", managerToken, new { roleId = role })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ClosesSoon(danTab)).Should().BeTrue("a new role means a new token");
        (await ClosesSoon(danOtherTab)).Should().BeTrue();
        await SendToUser(eva.Id, "still here");
        (await MarkerOf(evaTab)).Should().Be("still here");
    }

    [Fact]
    public async Task DeactivateUser_ClosesTheirConnections_AndTheTokenTheyStillHoldJoinsNoProject()
    {
        var managerToken = await TokenWith(Permissions.UsersManage);
        var owner = await CreateUser(UnusedEmail());
        var dan = await CreateUser(UnusedEmail());
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(dan));
        var danToken = Api.Factory.CreateToken(dan.Id);
        var danTab = await Connect(danToken);
        (await Join(danTab, project.Id)).Should().BeNull();

        (await Send(HttpMethod.Post, $"/api/users/{dan.Id}/deactivate", managerToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ClosesSoon(danTab)).Should().BeTrue();
        var reconnected = await Connect(danToken);
        (await Join(reconnected, project.Id)).Should().Be("Project.MemberRequired");
    }
}
