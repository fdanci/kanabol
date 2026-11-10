using System.Net;
using System.Text.Json;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Api.Tests.Dashboard;

/// <summary>
/// The main panel's numbers and the caller's own tasks over HTTP (Tasks.md group 17a; SPEC section 4.4,
/// "Panoul principal și task-urile mele"): GET /api/me/summary?weekStart= and GET /api/me/tasks, for anyone
/// signed in, each about their own only.
/// </summary>
public sealed class MeEndpointTests(ApiFixture api) : ProjectApiTest(api)
{
    private const string Summary = "/api/me/summary";
    private const string MyTasks = "/api/me/tasks";

    /// <summary>
    /// Dan in KAN and WEB, Ana owning both: KAN-1 Dan's, open, overdue, High; KAN-2 Dan's, finished now;
    /// KAN-3 Dan's, archived; WEB-1 Dan's, open, due tomorrow, Critical; WEB-2 Ana's only.
    /// </summary>
    private async Task<(User Dan, string DanToken, Project Web)> DansTasks()
    {
        var (ana, _) = await SignedIn();
        var (dan, danToken) = await SignedIn();
        var kan = await CreateProject(builder => builder.WithKey("KAN").WithName("Kanabol").OwnedBy(ana).WithMember(dan));
        var web = await CreateProject(builder => builder.WithKey("WEB").WithName("Site").OwnedBy(ana).WithMember(dan));
        var now = DateTimeOffset.UtcNow;

        // The builder numbers the tasks of a project in the order they are built.
        TaskItemBuilder In(Project project) => new TaskItemBuilder().InProject(project).CreatedBy(ana);
        TaskItem[] tasks =
        [
            In(kan).WithTitle("Login SSO").WithPriority(TaskPriority.High).AssignedTo(dan).DueAt(now.AddDays(-1)).Build(),
            In(kan).InColumn(kan.Columns.Single(column => column.Kind == ColumnKind.Done)).WithTitle("Logo nou").AssignedTo(dan).Build(),
            In(kan).WithTitle("Arhivat").AssignedTo(dan).Build(),
            In(web).WithTitle("Formular").WithPriority(TaskPriority.Critical).AssignedTo(dan).DueAt(now.AddDays(1)).Build(),
            In(web).WithTitle("Al Anei").AssignedTo(ana).Build(),
        ];
        tasks[2].Archive(ana.Id, now);

        await using var db = Api.Postgres.CreateContext();
        db.TaskItems.AddRange(tasks);
        await db.SaveChangesAsync();
        await db.TaskItems.Where(task => task.CompletedAt != null).ExecuteUpdateAsync(set => set.SetProperty(task => task.CompletedAt, now));
        return (dan, danToken, web);
    }

    private static string WeekStartParameter(DateTimeOffset weekStart) => Uri.EscapeDataString(weekStart.ToString("O"));

    private static List<JsonElement> Items(JsonDocument json) => [.. json.RootElement.GetProperty("items").EnumerateArray()];

    private static string Keys(JsonDocument json) => string.Join(' ', Items(json).Select(item => item.GetProperty("key").GetString()));

    /// <summary>The fields a 400 names, after checking it's the Validation one.</summary>
    private static async Task<HashSet<string>> FieldErrorsFrom(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = await JsonFrom(response);
        body.RootElement.GetProperty("code").GetString().Should().Be("Validation");
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(field => field.Name).ToHashSet();
    }

    [Fact]
    public async Task Summary_Returns200WithTheFourNumbers()
    {
        var (_, danToken, _) = await DansTasks();

        var response = await Send(HttpMethod.Get, $"{Summary}?weekStart={WeekStartParameter(DateTimeOffset.UtcNow.AddDays(-3))}", danToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        var summary = json.RootElement;
        (summary.GetProperty("openTasks").GetInt32(), summary.GetProperty("overdueTasks").GetInt32(),
            summary.GetProperty("completedThisWeek").GetInt32(), summary.GetProperty("activeProjects").GetInt32())
            .Should().Be((2, 1, 1, 2));
    }

    // The browser sends Monday midnight in its own time, with its offset.
    [Fact]
    public async Task Summary_TakesWeekStartWithAnOffset()
    {
        var (_, danToken, _) = await DansTasks();
        var weekStart = new DateTimeOffset(DateTime.UtcNow.AddDays(-1).Date, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(2));

        (await Send(HttpMethod.Get, $"{Summary}?weekStart={WeekStartParameter(weekStart)}", danToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?weekStart=")]
    [InlineData("?weekStart=luni")]
    [InlineData("?weekStart=2099-01-05T00:00:00Z")]
    public async Task Summary_WithoutAWeekStartOfThePast_Returns400OnWeekStart(string query)
    {
        var (_, danToken, _) = await DansTasks();

        (await FieldErrorsFrom(await Send(HttpMethod.Get, $"{Summary}{query}", danToken))).Should().Equal("weekStart");
    }

    [Fact]
    public async Task Summary_WithoutASession_Returns401() =>
        (await Send(HttpMethod.Get, $"{Summary}?weekStart={WeekStartParameter(DateTimeOffset.UtcNow)}", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Tasks_Returns200WithAPageOfTheCallersByDeadlineEachWithItsProject()
    {
        var (_, danToken, web) = await DansTasks();

        var response = await Send(HttpMethod.Get, MyTasks, danToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = await JsonFrom(response);
        (json.RootElement.GetProperty("page").GetInt32(), json.RootElement.GetProperty("pageSize").GetInt32(), json.RootElement.GetProperty("totalCount").GetInt32())
            .Should().Be((1, 50, 3));
        Keys(json).Should().Be("KAN-1 WEB-1 KAN-2");
        var row = Items(json)[1];
        row.GetProperty("projectId").GetGuid().Should().Be(web.Id);
        row.GetProperty("projectKey").GetString().Should().Be("WEB");
        row.GetProperty("projectName").GetString().Should().Be("Site");
        row.GetProperty("title").GetString().Should().Be("Formular");
        row.GetProperty("priority").GetString().Should().Be("Critical");
        row.GetProperty("columnName").GetString().Should().Be("Backlog");
        row.GetProperty("columnKind").GetString().Should().Be("Backlog");
    }

    [Fact]
    public async Task Tasks_TakesOpenPriorityOverdueSearchSortAndPagesFromTheQuery()
    {
        var (_, danToken, _) = await DansTasks();

        using var open = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?open=true&pageSize=8", danToken));
        using var priorities = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?priority=High&priority=Critical&sort=key:desc", danToken));
        using var overdue = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?overdue=true", danToken));
        using var search = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?search=logo", danToken));
        using var second = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?page=2&pageSize=2", danToken));

        Keys(open).Should().Be("KAN-1 WEB-1");
        open.RootElement.GetProperty("pageSize").GetInt32().Should().Be(8);
        Keys(priorities).Should().Be("WEB-1 KAN-1");
        Keys(overdue).Should().Be("KAN-1");
        Keys(search).Should().Be("KAN-2");
        Keys(second).Should().Be("KAN-2");
    }

    // The project list's filters but the assignee: someone else's id changes nothing.
    [Fact]
    public async Task Tasks_WithAnAssigneeId_StillGivesTheCallersOwn()
    {
        var (_, danToken, _) = await DansTasks();

        using var json = await JsonFrom(await Send(HttpMethod.Get, $"{MyTasks}?assigneeId={Guid.CreateVersion7()}", danToken));

        Keys(json).Should().Be("KAN-1 WEB-1 KAN-2");
    }

    [Fact]
    public async Task Tasks_WithIncludeArchivedWithoutTasksArchive_Returns403PermissionRequired()
    {
        var (_, danToken, _) = await DansTasks();

        var response = await Send(HttpMethod.Get, $"{MyTasks}?includeArchived=true", danToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(response)).Should().Be("Auth.PermissionRequired");
    }

    [Theory]
    [InlineData("open=maybe", "open")]
    [InlineData("sort=deadline:asc", "sort")]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("priority=Urgent", "priority")]
    public async Task Tasks_WithAWrongQuery_Returns400OnIt(string query, string field)
    {
        var (_, danToken, _) = await DansTasks();

        (await FieldErrorsFrom(await Send(HttpMethod.Get, $"{MyTasks}?{query}", danToken))).Should().Contain(field);
    }

    [Fact]
    public async Task Tasks_ForSomeoneInNoProject_ReturnsAnEmptyPage()
    {
        await DansTasks();
        var (_, evaToken) = await SignedIn();

        using var json = await JsonFrom(await Send(HttpMethod.Get, MyTasks, evaToken));

        json.RootElement.GetProperty("totalCount").GetInt32().Should().Be(0);
    }
}
