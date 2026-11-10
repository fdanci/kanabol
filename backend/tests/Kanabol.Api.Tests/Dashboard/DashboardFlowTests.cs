using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;

namespace Kanabol.Api.Tests.Dashboard;

/// <summary>
/// The group's flows end to end over HTTP (Tasks.md group 17a; SPEC section 4.4, "Panoul principal și
/// task-urile mele"): each change to a task, to its project or to its members changes the main panel's
/// numbers, "Task-urile mele" and "Proiectele mele" as the rules say, read again as the panel reads them.
/// </summary>
public sealed class DashboardFlowTests(ApiFixture api) : ProjectApiTest(api)
{
    private sealed record Panel(int Open, int Overdue, int CompletedThisWeek, int Projects);

    /// <summary>Ana owns KAN and WEB, with tasks.create and tasks.archive; Dan is a member of both.</summary>
    private async Task<(User Ana, string AnaToken, User Dan, string DanToken, Project Kan, Project Web)> ATeam()
    {
        var (ana, anaToken) = await SignedIn(Permissions.TasksCreate, Permissions.TasksArchive);
        var (dan, danToken) = await SignedIn();
        var kan = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(ana).WithMember(dan));
        var web = await CreateProject(builder => builder.WithKey("WEB").WithName("Site").OwnedBy(ana).WithMember(dan));
        return (ana, anaToken, dan, danToken, kan, web);
    }

    private async Task<Guid> CreateTask(string token, Project project, string title, Guid[] assigneeIds, DateTimeOffset? dueDate = null)
    {
        var response = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title, priority = "Medium", assigneeIds, dueDate });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status) =>
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());

    private async Task<Panel> PanelOf(string token)
    {
        var weekStart = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var response = await Send(HttpMethod.Get, $"/api/me/summary?weekStart={weekStart}", token);
        await Expect(response, HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var summary = json.RootElement;
        return new Panel(
            summary.GetProperty("openTasks").GetInt32(),
            summary.GetProperty("overdueTasks").GetInt32(),
            summary.GetProperty("completedThisWeek").GetInt32(),
            summary.GetProperty("activeProjects").GetInt32());
    }

    private async Task<string> MyTaskKeys(string token, string query = "")
    {
        var response = await Send(HttpMethod.Get, $"/api/me/tasks{query}", token);
        await Expect(response, HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        return string.Join(' ', json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("key").GetString()));
    }

    /// <summary>key:open:finished of each of the caller's projects, by name.</summary>
    private async Task<string> MyProjects(string token)
    {
        var response = await Send(HttpMethod.Get, "/api/projects?mine=true", token);
        await Expect(response, HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        return string.Join(' ', json.RootElement.GetProperty("items").EnumerateArray().Select(project =>
            $"{project.GetProperty("key").GetString()}:{project.GetProperty("openTaskCount").GetInt32()}:{project.GetProperty("completedTaskCount").GetInt32()}"));
    }

    private static Guid DoneColumnOf(Project project) => project.Columns.Single(column => column.Kind == ColumnKind.Done).Id;

    private static Guid FirstColumnOf(Project project) => project.Columns.Single(column => column.Position == 0).Id;

    [Fact]
    public async Task ATaskOfMine_FinishedArchivedRestoredAndReopened_MovesThePanelsNumbers()
    {
        var (_, anaToken, dan, danToken, kan, _) = await ATeam();
        (await PanelOf(danToken)).Should().Be(new Panel(0, 0, 0, 2));

        var taskId = await CreateTask(anaToken, kan, "Login SSO", [dan.Id], DateTimeOffset.UtcNow.AddDays(-1));
        (await PanelOf(danToken)).Should().Be(new Panel(1, 1, 0, 2));
        (await MyProjects(danToken)).Should().Be("KAN:1:0 WEB:0:0");

        await Expect(await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", anaToken, new { columnId = DoneColumnOf(kan) }), HttpStatusCode.OK);
        (await PanelOf(danToken)).Should().Be(new Panel(0, 0, 1, 2));
        (await MyProjects(danToken)).Should().Be("KAN:0:1 WEB:0:0");
        (await MyTaskKeys(danToken, "?open=true")).Should().BeEmpty();
        (await MyTaskKeys(danToken)).Should().Be("KAN-1");

        await Expect(await Send(HttpMethod.Delete, $"/api/tasks/{taskId}", anaToken), HttpStatusCode.NoContent);
        (await PanelOf(danToken)).Should().Be(new Panel(0, 0, 0, 2));
        (await MyProjects(danToken)).Should().Be("KAN:0:0 WEB:0:0");
        (await MyTaskKeys(danToken)).Should().BeEmpty();

        await Expect(await Send(HttpMethod.Post, $"/api/tasks/{taskId}/restore", anaToken), HttpStatusCode.OK);
        (await PanelOf(danToken)).Should().Be(new Panel(0, 0, 1, 2));

        await Expect(await Send(HttpMethod.Patch, $"/api/tasks/{taskId}/position", anaToken, new { columnId = FirstColumnOf(kan) }), HttpStatusCode.OK);
        (await PanelOf(danToken)).Should().Be(new Panel(1, 1, 0, 2));
    }

    [Fact]
    public async Task ADeadlineMovedAway_TakesTheTaskOutOfTheOverdueOnes()
    {
        var (_, anaToken, dan, danToken, kan, _) = await ATeam();
        var taskId = await CreateTask(anaToken, kan, "Login SSO", [dan.Id], DateTimeOffset.UtcNow.AddDays(-1));

        await Expect(await Send(HttpMethod.Put, $"/api/tasks/{taskId}/due-date", anaToken, new { dueDate = DateTimeOffset.UtcNow.AddDays(3) }), HttpStatusCode.OK);

        (await PanelOf(danToken)).Should().Be(new Panel(1, 0, 0, 2));
        (await MyTaskKeys(danToken, "?overdue=true")).Should().BeEmpty();
    }

    [Fact]
    public async Task AnAssignment_AddsTheTaskToMineAndTakingMeOffRemovesIt()
    {
        var (ana, anaToken, dan, danToken, kan, web) = await ATeam();
        await CreateTask(anaToken, kan, "Al Anei", [ana.Id]);
        var taskId = await CreateTask(anaToken, web, "Formular", []);
        (await MyTaskKeys(danToken)).Should().BeEmpty();

        await Expect(await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", anaToken, new { assigneeIds = new[] { dan.Id, ana.Id } }), HttpStatusCode.OK);
        (await MyTaskKeys(danToken)).Should().Be("WEB-1");
        (await MyTaskKeys(anaToken)).Should().Be("WEB-1 KAN-1");

        await Expect(await Send(HttpMethod.Put, $"/api/tasks/{taskId}/assignees", anaToken, new { assigneeIds = new[] { ana.Id } }), HttpStatusCode.OK);
        (await MyTaskKeys(danToken)).Should().BeEmpty();
        (await PanelOf(danToken)).Should().Be(new Panel(0, 0, 0, 2));
    }

    // SPEC section 4.4: "mine" is in projects not archived, so archiving one takes its tasks and the project away.
    [Fact]
    public async Task AProjectArchived_LeavesThePanelUntilItsRestored()
    {
        var (_, anaToken, dan, danToken, kan, web) = await ATeam();
        await CreateTask(anaToken, kan, "Login SSO", [dan.Id]);
        await CreateTask(anaToken, web, "Formular", [dan.Id]);

        await Expect(await Send(HttpMethod.Delete, $"/api/projects/{web.Id}", anaToken), HttpStatusCode.NoContent);
        (await PanelOf(danToken)).Should().Be(new Panel(1, 0, 0, 1));
        (await MyTaskKeys(danToken)).Should().Be("KAN-1");
        (await MyProjects(danToken)).Should().Be("KAN:1:0");

        await Expect(await Send(HttpMethod.Post, $"/api/projects/{web.Id}/restore", anaToken), HttpStatusCode.OK);
        (await PanelOf(danToken)).Should().Be(new Panel(2, 0, 0, 2));
        (await MyTaskKeys(danToken)).Should().Be("WEB-1 KAN-1");
    }

    // Someone taken out of a project stays on its tasks until someone takes them off, but they aren't
    // theirs on the panel any more: the panel would lead to a project they can't open.
    [Fact]
    public async Task AMemberTakenOutOfAProject_NoLongerCountsItsTasks()
    {
        var (_, anaToken, dan, danToken, kan, web) = await ATeam();
        await CreateTask(anaToken, kan, "Login SSO", [dan.Id], DateTimeOffset.UtcNow.AddDays(-1));
        await CreateTask(anaToken, web, "Formular", [dan.Id]);

        await Expect(await Send(HttpMethod.Delete, $"/api/projects/{kan.Id}/members/{dan.Id}", anaToken), HttpStatusCode.NoContent);

        (await PanelOf(danToken)).Should().Be(new Panel(1, 0, 0, 1));
        (await MyTaskKeys(danToken)).Should().Be("WEB-1");
        (await MyProjects(danToken)).Should().Be("WEB:1:0");
    }

    [Fact]
    public async Task TheCallersOwn_AreNeverAnotherOnes()
    {
        var (_, anaToken, dan, _, kan, _) = await ATeam();
        await CreateTask(anaToken, kan, "Login SSO", [dan.Id]);
        var (_, viewerToken) = await SignedIn(Permissions.ProjectsViewAll, Permissions.TasksArchive);

        (await PanelOf(viewerToken)).Should().Be(new Panel(0, 0, 0, 0));
        (await MyTaskKeys(viewerToken, "?includeArchived=true")).Should().BeEmpty();
        (await MyProjects(viewerToken)).Should().BeEmpty();
        using var all = await JsonFrom(await Send(HttpMethod.Get, "/api/projects", viewerToken));
        all.RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    // The panel reads the same notifications the bell does, the last 5.
    [Fact]
    public async Task AnAssignment_IsAmongTheLastNotificationsThePanelReads()
    {
        var (_, anaToken, dan, danToken, kan, _) = await ATeam();
        await CreateTask(anaToken, kan, "Login SSO", [dan.Id]);

        using var json = await JsonFrom(await Send(HttpMethod.Get, "/api/notifications?pageSize=5", danToken));

        var item = json.RootElement.GetProperty("items").EnumerateArray().Single();
        (item.GetProperty("type").GetString(), item.GetProperty("task").GetProperty("key").GetString()).Should().Be(("TaskAssigned", "KAN-1"));
        item.GetProperty("readAt").ValueKind.Should().Be(JsonValueKind.Null);
    }
}
