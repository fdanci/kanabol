using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Realtime;

/// <summary>
/// The group's flows end to end (Tasks.md group 14; SPEC section 9): real requests change a task, and real
/// connections to the hub, opened as a browser opens them, hear it with the small payloads.
/// </summary>
public sealed class BoardRealtimeFlowTests(ApiFixture api) : RealtimeApiTest(api)
{
    private static readonly string[] _taskEvents = ["TaskCreated", "TaskMoved", "TaskUpdated", "TaskArchived", "TaskRestored", "NotificationCreated", "Probe"];

    private async Task<(User Ana, User Dan, string AnaToken, string DanToken, Project Project, Guid TaskId)> ABoardWithATask()
    {
        var (ana, anaToken) = await SignedIn(Permissions.TasksCreate);
        var (dan, danToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("RT").OwnedBy(ana).WithMember(dan));
        using var created = await JsonFrom(await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", anaToken, new { title = "Primul", priority = "Low" }));
        return (ana, dan, anaToken, danToken, project, created.RootElement.GetProperty("id").GetGuid());
    }

    private async Task<HttpResponseMessage> SendFrom(Heard? tab, HttpMethod method, string path, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        if (tab is not null)
        {
            request.Headers.Add(HttpRequestContext.ConnectionIdHeader, tab.Connection.ConnectionId);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    private async Task<Heard> OnTheBoard(string token, Project project)
    {
        var heard = await Connect(token, _taskEvents);
        await heard.Connection.InvokeAsync("JoinProject", project.Id);
        return heard;
    }

    // Through the queue the events take, so it comes after whatever the requests before it sent.
    private Task Probe(Guid userId) =>
        Api.Factory.Services.GetRequiredService<IBoardNotifier>().NotifyUserAsync(userId, "Probe", new { marker = "the end" }, CancellationToken.None);

    private static IEnumerable<string> NamesIn(JsonElement payload) => payload.EnumerateObject().Select(property => property.Name);

    [Fact]
    public async Task PatchPosition_ByAMember_ReachesAnotherBoardOverAnAuthenticatedHandshake()
    {
        var (_, dan, anaToken, danToken, project, taskId) = await ABoardWithATask();
        var anasBoard = await OnTheBoard(anaToken, project);
        var dansTab = await Connect(danToken);
        // Gata asks for nothing; În lucru would want an estimate, and the board would hear of it first.
        var target = project.Columns.Single(column => column.Kind == Kanabol.Domain.Projects.ColumnKind.Done);

        (await SendFrom(dansTab, HttpMethod.Patch, $"/api/tasks/{taskId}/position", danToken, new { columnId = target.Id, position = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var moved = await anasBoard.NextIs("TaskMoved");
        NamesIn(moved).Should().Equal("taskId", "projectId", "byUserId", "originConnectionId", "columnId", "position");
        moved.GetProperty("taskId").GetGuid().Should().Be(taskId);
        moved.GetProperty("projectId").GetGuid().Should().Be(project.Id);
        moved.GetProperty("byUserId").GetGuid().Should().Be(dan.Id);
        moved.GetProperty("originConnectionId").GetString().Should().Be(dansTab.Connection.ConnectionId);
        moved.GetProperty("columnId").GetGuid().Should().Be(target.Id);
        moved.GetProperty("position").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task AnOutsider_IsRefusedTheProjectAndHearsNoneOfItsChanges()
    {
        var (_, _, anaToken, _, project, taskId) = await ABoardWithATask();
        var (eva, evaToken) = await SignedIn();
        var evasTab = await Connect(evaToken, _taskEvents);

        (await evasTab.Connection.InvokeAsync<string?>("JoinProject", project.Id)).Should().Be("Project.MemberRequired");
        (await SendFrom(null, HttpMethod.Put, $"/api/tasks/{taskId}/title", anaToken, new { title = "Redenumit" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendFrom(null, HttpMethod.Delete, $"/api/tasks/{taskId}", anaToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await Probe(eva.Id);
        (await evasTab.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end", "nothing of the project came before it");
    }

    // The tab that made the change hears it too, with its own id as the origin, which is how BoardStore
    // knows to ignore it (board-store.spec.ts); a request from no tab carries no origin, and nobody ignores it.
    [Fact]
    public async Task AChange_ComesBackToTheTabThatMadeIt_WithItsOwnConnectionAsTheOrigin()
    {
        var (ana, _, anaToken, _, project, taskId) = await ABoardWithATask();
        var anasBoard = await OnTheBoard(anaToken, project);

        (await SendFrom(anasBoard, HttpMethod.Put, $"/api/tasks/{taskId}/priority", anaToken, new { priority = "High" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendFrom(null, HttpMethod.Put, $"/api/tasks/{taskId}/priority", anaToken, new { priority = "Critical" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var fromTheTab = await anasBoard.NextIs("TaskUpdated");
        fromTheTab.GetProperty("originConnectionId").GetString().Should().Be(anasBoard.Connection.ConnectionId);
        fromTheTab.GetProperty("byUserId").GetGuid().Should().Be(ana.Id);
        var fromNoTab = await anasBoard.NextIs("TaskUpdated");
        fromNoTab.GetProperty("originConnectionId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task PutAssignees_SendsTaskUpdatedWithAssigneesToTheBoard_AndNotificationCreatedToTheNewAssigneeOnly()
    {
        var (ana, dan, anaToken, danToken, project, taskId) = await ABoardWithATask();
        var anasBoard = await OnTheBoard(anaToken, project);
        var dansTab = await Connect(danToken, _taskEvents);

        (await SendFrom(anasBoard, HttpMethod.Put, $"/api/tasks/{taskId}/assignees", anaToken, new { assigneeIds = new[] { dan.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await anasBoard.NextIs("TaskUpdated");
        NamesIn(updated).Should().Equal("taskId", "projectId", "byUserId", "originConnectionId", "field");
        updated.GetProperty("field").GetString().Should().Be("Assignees");
        updated.GetProperty("taskId").GetGuid().Should().Be(taskId);

        await using var db = Api.Postgres.CreateContext();
        var notification = await db.Notifications.SingleAsync(found => found.UserId == dan.Id && found.Type == NotificationType.TaskAssigned);
        var told = await dansTab.NextIs("NotificationCreated");
        NamesIn(told).Should().Equal("notificationId");
        told.GetProperty("notificationId").GetGuid().Should().Be(notification.Id);

        await Probe(ana.Id);
        (await anasBoard.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end", "Ana assigned Dan, and hears of no notification of her own");
    }

    // Group 22 (SPEC section 4.4): the estimate a move into În lucru brings is a change of its own, heard before
    // the move, as PUT …/estimate is, so another board reads the card's "lucrat / estimat" again.
    [Fact]
    public async Task PatchPosition_IntoInProgressWithAnEstimate_SendsTaskUpdatedWithEstimatedMinutesBeforeTaskMoved()
    {
        var (_, _, anaToken, danToken, project, taskId) = await ABoardWithATask();
        var dansBoard = await OnTheBoard(danToken, project);
        var inProgress = project.Columns.Single(column => column.Kind == ColumnKind.InProgress).Id;

        (await SendFrom(null, HttpMethod.Patch, $"/api/tasks/{taskId}/position", anaToken, new { columnId = inProgress, estimatedMinutes = 90 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendFrom(null, HttpMethod.Put, $"/api/tasks/{taskId}/estimate", anaToken, new { estimatedMinutes = 120 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var estimated = await dansBoard.NextIs("TaskUpdated");
        NamesIn(estimated).Should().Equal("taskId", "projectId", "byUserId", "originConnectionId", "field");
        estimated.GetProperty("field").GetString().Should().Be("EstimatedMinutes");
        (await dansBoard.NextIs("TaskMoved")).GetProperty("columnId").GetGuid().Should().Be(inProgress);
        (await dansBoard.NextIs("TaskUpdated")).GetProperty("field").GetString().Should().Be("EstimatedMinutes");
    }
}
