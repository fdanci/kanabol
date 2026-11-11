using System.Net;
using System.Text.Json;
using Kanabol.Api.Realtime;
using Kanabol.Api.Tests.Realtime;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Features.Notifications.Commands.NotifyOverdueTasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Notifications;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Notifications;

/// <summary>
/// The group's flows end to end (Tasks.md group 17; SPEC sections 4.3, 4.4 and 9): tasks made and changed
/// through real requests, the overdue check run as the worker runs it, in a scope of its own without a user,
/// the notifications read through the API, and the alert heard on the user's own group over a real
/// WebSocket. The worker itself is off in these tests (Alerts:OverdueCheckEnabled).
/// </summary>
public sealed class OverdueAlertsFlowTests(ApiFixture api) : RealtimeApiTest(api)
{
    private static readonly DateTimeOffset _past = DateTimeOffset.UtcNow.AddDays(-2);
    private static readonly DateTimeOffset _future = DateTimeOffset.UtcNow.AddDays(30);

    private sealed record Team(User Ana, string AnaToken, User Dan, string DanToken, User Ioana, string IoanaToken, string EvaToken, Project Project);

    /// <summary>KAN, owned by Ana, with Dan and Ioana as members; Eva is from outside.</summary>
    private async Task<Team> ATeam()
    {
        var (ana, anaToken) = await SignedIn(Permissions.TasksCreate);
        var (dan, danToken) = await SignedIn();
        var (ioana, ioanaToken) = await SignedIn();
        var (_, evaToken) = await SignedIn();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(ana).WithMember(dan).WithMember(ioana));
        return new Team(ana, anaToken, dan, danToken, ioana, ioanaToken, evaToken, project);
    }

    private async Task<Guid> ATask(Team team, string title, DateTimeOffset? dueDate, params User[] assignees)
    {
        var response = await Send(HttpMethod.Post, $"/api/projects/{team.Project.Id}/tasks", team.AnaToken, new
        {
            title,
            priority = "Medium",
            dueDate,
            assigneeIds = assignees.Select(user => user.Id).ToArray(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>One run, as OverdueTasksWorker makes it: a scope of its own, nobody signed in.</summary>
    private async Task<int> RunOverdueCheck()
    {
        await using var scope = Api.Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<NotifyOverdueTasksCommand, int>>()
            .Handle(new NotifyOverdueTasksCommand(), CancellationToken.None);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>The caller's overdue alerts, through the API: key and message.</summary>
    private async Task<List<string>> OverdueAlertsOf(string token)
    {
        var response = await Send(HttpMethod.Get, "/api/notifications?type=TaskOverdue&pageSize=100", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        return [.. json.RootElement.GetProperty("items").EnumerateArray().Select(item => $"{item.GetProperty("task").GetProperty("key").GetString()}|{item.GetProperty("message").GetString()}")];
    }

    private async Task<int> UnreadCountOf(string token)
    {
        using var json = await JsonFrom(await Send(HttpMethod.Get, "/api/notifications/unread-count", token));
        return json.RootElement.GetProperty("count").GetInt32();
    }

    // Through the queue the events take, so it comes after whatever the requests before it sent.
    private Task Probe(Guid userId) =>
        Api.Factory.Services.GetRequiredService<IBoardNotifier>().NotifyUserAsync(userId, "Probe", new { marker = "the end" }, CancellationToken.None);

    [Fact]
    public async Task OverdueTask_TellsItsAssigneeAndTheOwnerOnce_AndNobodyElse()
    {
        var team = await ATeam();
        await ATask(team, "Login SSO", _past, team.Dan);

        (await RunOverdueCheck()).Should().Be(2);
        (await RunOverdueCheck()).Should().Be(0);

        (await OverdueAlertsOf(team.DanToken)).Should().Equal("KAN-1|KAN-1: Login SSO is overdue");
        (await OverdueAlertsOf(team.AnaToken)).Should().Equal("KAN-1|KAN-1: Login SSO is overdue");
        (await OverdueAlertsOf(team.IoanaToken)).Should().BeEmpty();
        (await OverdueAlertsOf(team.EvaToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task AssigneeAddedLater_IsToldAtTheNextRun_AndNobodyTwice_EvenAfterTheDeadlinePassesAgain()
    {
        var team = await ATeam();
        var taskId = await ATask(team, "Login SSO", _past, team.Dan);
        await RunOverdueCheck();

        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", team.AnaToken, new { assigneeIds = new[] { team.Dan.Id, team.Ioana.Id } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/due-date", team.AnaToken, new { dueDate = _future })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Put, $"/api/tasks/{taskId}/due-date", team.AnaToken, new { dueDate = _past })).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RunOverdueCheck()).Should().Be(1);

        (await OverdueAlertsOf(team.IoanaToken)).Should().Equal("KAN-1|KAN-1: Login SSO is overdue");
        (await OverdueAlertsOf(team.DanToken)).Should().HaveCount(1);
        (await OverdueAlertsOf(team.AnaToken)).Should().HaveCount(1);
    }

    [Fact]
    public async Task TaskDoneArchivedNotDueOrInAnArchivedProject_TellsNobody()
    {
        var team = await ATeam();
        var done = await ATask(team, "Gata", _past, team.Dan);
        var archived = await ATask(team, "Arhivat", _past, team.Dan);
        await ATask(team, "Mai tarziu", _future, team.Dan);
        await ATask(team, "Fara deadline", null, team.Dan);
        var doneColumn = team.Project.Columns.Single(column => column.Kind == ColumnKind.Done);
        (await Send(HttpMethod.Patch, $"/api/tasks/{done}/position", team.AnaToken, new { columnId = doneColumn.Id })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(HttpMethod.Delete, $"/api/tasks/{archived}", team.AnaToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var other = await CreateProject(builder => builder.WithKey("OLD").OwnedBy(team.Ana).WithMember(team.Dan));
        (await Send(HttpMethod.Post, $"/api/projects/{other.Id}/tasks", team.AnaToken, new { title = "Vechi", priority = "Medium", dueDate = _past, assigneeIds = new[] { team.Dan.Id } }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await Send(HttpMethod.Delete, $"/api/projects/{other.Id}", team.AnaToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RunOverdueCheck()).Should().Be(0);

        (await OverdueAlertsOf(team.DanToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Alert_IsHeardOnTheUsersOwnGroup_WithoutABoard_AndByNoOutsider()
    {
        var team = await ATeam();
        await ATask(team, "Login SSO", _past, team.Dan);
        var dansBell = await Connect(team.DanToken, "NotificationCreated", "Probe");
        var evasBell = await Connect(team.EvaToken, "NotificationCreated", "Probe");
        var ioanasBell = await Connect(team.IoanaToken, "NotificationCreated", "Probe");
        var evaId = await IdOf(team.EvaToken);

        await RunOverdueCheck();

        await using var db = Api.Postgres.CreateContext();
        var dans = await db.Notifications.SingleAsync(found => found.UserId == team.Dan.Id && found.Type == NotificationType.TaskOverdue);
        var told = await dansBell.NextIs("NotificationCreated");
        told.EnumerateObject().Select(property => property.Name).Should().Equal("notificationId");
        told.GetProperty("notificationId").GetGuid().Should().Be(dans.Id);

        await Probe(evaId);
        await Probe(team.Ioana.Id);
        (await evasBell.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end", "Eva is from outside");
        (await ioanasBell.NextIs("Probe")).GetProperty("marker").GetString().Should().Be("the end", "Ioana is a member, neither an owner nor an assignee");
    }

    [Fact]
    public async Task ReadAndReadAll_CountOnlyWhatTheyMarkNow_AndNobodyReadsAnothers()
    {
        var team = await ATeam();
        var taskId = await ATask(team, "Login SSO", _past, team.Dan);
        await RunOverdueCheck();
        (await UnreadCountOf(team.DanToken)).Should().Be(2, "Dan was assigned, then told the task is overdue");
        using var page = await JsonFrom(await Send(HttpMethod.Get, "/api/notifications?type=TaskOverdue", team.DanToken));
        var alertId = page.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        var first = await Send(HttpMethod.Post, $"/api/notifications/{alertId}/read", team.DanToken);
        using var firstJson = await JsonFrom(first);
        var again = await Send(HttpMethod.Post, $"/api/notifications/{alertId}/read", team.DanToken);
        using var againJson = await JsonFrom(again);
        var anothers = await Send(HttpMethod.Post, $"/api/notifications/{alertId}/read", team.EvaToken);

        againJson.RootElement.GetProperty("readAt").GetDateTimeOffset().Should().Be(firstJson.RootElement.GetProperty("readAt").GetDateTimeOffset());
        anothers.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodeFrom(anothers)).Should().Be("Notification.NotFound");
        (await UnreadCountOf(team.DanToken)).Should().Be(1);

        using var all = await JsonFrom(await Send(HttpMethod.Post, "/api/notifications/read-all", team.DanToken));
        all.RootElement.GetProperty("count").GetInt32().Should().Be(1);

        (await Send(HttpMethod.Post, $"/api/tasks/{taskId}/comments", team.IoanaToken, new { body = "Merge" })).StatusCode.Should().Be(HttpStatusCode.Created);
        using var afterComment = await JsonFrom(await Send(HttpMethod.Post, "/api/notifications/read-all", team.DanToken));
        afterComment.RootElement.GetProperty("count").GetInt32().Should().Be(1, "only the comment came since");
        (await UnreadCountOf(team.DanToken)).Should().Be(0);
    }

    [Fact]
    public async Task Alert_StaysInTheList_WhenItsUserNoLongerSeesTheTask()
    {
        var team = await ATeam();
        var taskId = await ATask(team, "Login SSO", _past, team.Dan);
        await RunOverdueCheck();

        (await Send(HttpMethod.Delete, $"/api/projects/{team.Project.Id}/members/{team.Dan.Id}", team.AnaToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await OverdueAlertsOf(team.DanToken)).Should().Equal("KAN-1|KAN-1: Login SSO is overdue");
        (await Send(HttpMethod.Get, $"/api/tasks/{taskId}", team.DanToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Guid> IdOf(string token)
    {
        using var json = await JsonFrom(await Send(HttpMethod.Get, "/api/auth/me", token));
        return json.RootElement.GetProperty("id").GetGuid();
    }
}
