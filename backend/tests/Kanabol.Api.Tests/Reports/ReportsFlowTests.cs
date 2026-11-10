using System.Globalization;
using System.Net;
using Kanabol.Api.Tests.Projects;
using Kanabol.Api.Tests.Support;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kanabol.Api.Tests.Reports;

/// <summary>
/// The group's flows end to end over HTTP (Tasks.md group 18; SPEC section 5.1.1): a task finished, moved
/// back, finished again, archived and restored, its deadline moved, each changing what the three reports
/// say, read as the page reads them, by reports.view and by the project's owner without it.
/// </summary>
public sealed class ReportsFlowTests(ApiFixture api) : ProjectApiTest(api)
{
    /// <summary>What the page shows of the three reports, in one line.</summary>
    private sealed record Numbers(int Finished, string ByUser, int Measured, int Overdue, int OnTime, int Late, string Rate);

    /// <summary>Ana owns REP, with tasks.create and tasks.archive, without reports.view; Dan is a member.</summary>
    private async Task<(User Ana, string AnaToken, User Dan, string DanToken, Project Rep)> ATeam()
    {
        var ana = await Person("Ana Pop", Permissions.TasksCreate, Permissions.TasksArchive);
        var dan = await Person("Dan Ionescu");
        var (anaToken, danToken) = (await TokenOf(ana), await TokenOf(dan));
        var rep = await CreateProject(builder => builder.WithKey("REP").WithName("Rapoarte").OwnedBy(ana).WithMember(dan));
        return (ana, anaToken, dan, danToken, rep);
    }

    /// <summary>A named person in a role of their own with these permissions, who signs in with Password.</summary>
    private async Task<User> Person(string displayName, params string[] permissions)
    {
        await using var db = Api.Postgres.CreateContext();
        var email = UnusedEmail();
        var role = new Role($"Role of {email}", isSystem: false);
        (await db.Permissions.Where(permission => permissions.Contains(permission.Name)).ToListAsync()).ForEach(permission => role.Grant(permission.Id));
        var user = new User(email, displayName, PasswordHash, role.Id);
        db.Roles.Add(role);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<string> TokenOf(User user) => await AccessTokenFrom(await Login(user.Email));

    private async Task<Guid> CreateTask(string token, Project project, string title, Guid[] assigneeIds, DateTimeOffset? dueDate)
    {
        var response = await Send(HttpMethod.Post, $"/api/projects/{project.Id}/tasks", token, new { title, priority = "Medium", assigneeIds, dueDate });
        await Expect(response, HttpStatusCode.Created);
        using var json = await JsonFrom(response);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status) =>
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());

    private Task Move(string token, Guid task, Guid columnId) =>
        Send(HttpMethod.Patch, $"/api/tasks/{task}/position", token, new { columnId }).ContinueWith(sent => Expect(sent.Result, HttpStatusCode.OK)).Unwrap();

    /// <summary>A day either side of now, for the project, as the page asks for it.</summary>
    private async Task<Numbers> NumbersOf(string token, Project project)
    {
        var now = DateTimeOffset.UtcNow;
        var query = $"from={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(now.AddDays(1).ToString("O"))}&projectId={project.Id}";

        var completed = await Send(HttpMethod.Get, $"/api/reports/completed?{query}", token);
        await Expect(completed, HttpStatusCode.OK);
        using var finished = await JsonFrom(completed);

        var cycleTime = await Send(HttpMethod.Get, $"/api/reports/cycle-time?{query}", token);
        await Expect(cycleTime, HttpStatusCode.OK);
        using var times = await JsonFrom(cycleTime);

        var deadlines = await Send(HttpMethod.Get, $"/api/reports/deadlines?{query}", token);
        await Expect(deadlines, HttpStatusCode.OK);
        using var kept = await JsonFrom(deadlines);
        var rate = kept.RootElement.GetProperty("onTimeRate");

        return new Numbers(
            finished.RootElement.GetProperty("total").GetInt32(),
            string.Join(' ', finished.RootElement.GetProperty("byUser").EnumerateArray().Select(row => row.GetProperty("count").GetInt32())),
            times.RootElement.GetProperty("count").GetInt32(),
            kept.RootElement.GetProperty("overdue").GetInt32(),
            kept.RootElement.GetProperty("completedOnTime").GetInt32(),
            kept.RootElement.GetProperty("completedLate").GetInt32(),
            rate.ValueKind == System.Text.Json.JsonValueKind.Null ? "null" : rate.GetDouble().ToString(CultureInfo.InvariantCulture));
    }

    private static Guid DoneColumnOf(Project project) => project.Columns.Single(column => column.Kind == ColumnKind.Done).Id;

    // Out of Gata a task goes back to În lucru, not before it (SPEC section 4.4), and is open again.
    private static Guid InProgressColumnOf(Project project) => project.Columns.Single(column => column.Kind == ColumnKind.InProgress).Id;

    [Fact]
    public async Task ATask_FinishedMovedBackAndFinishedAgain_IsCountedOnlyWhileFinished()
    {
        var (ana, anaToken, _, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Raport lunar", [ana.Id], DateTimeOffset.UtcNow.AddDays(2));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(0, "", 0, 0, 0, 0, "null"));

        await Move(anaToken, task, DoneColumnOf(rep));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 1, 0, "1"));

        await Move(anaToken, task, InProgressColumnOf(rep));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(0, "", 0, 0, 0, 0, "null"));

        await Move(anaToken, task, DoneColumnOf(rep));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 1, 0, "1"));
    }

    // Archiving after the work doesn't undo it (SPEC section 5.1.1).
    [Fact]
    public async Task ATaskFinished_ArchivedAndRestored_StaysCounted()
    {
        var (ana, anaToken, _, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Logo nou", [ana.Id], null);
        await Move(anaToken, task, DoneColumnOf(rep));

        await Expect(await Send(HttpMethod.Delete, $"/api/tasks/{task}", anaToken), HttpStatusCode.NoContent);
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 0, 0, "null"));

        await Expect(await Send(HttpMethod.Post, $"/api/tasks/{task}/restore", anaToken), HttpStatusCode.OK);
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 0, 0, "null"));
    }

    // An open task archived before it was finished counts nowhere, not even as overdue.
    [Fact]
    public async Task AnOpenTaskOverdue_Archived_LeavesTheReports()
    {
        var (ana, anaToken, _, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Uitat", [ana.Id], DateTimeOffset.UtcNow.AddHours(-3));
        (await NumbersOf(anaToken, rep)).Overdue.Should().Be(1);

        await Expect(await Send(HttpMethod.Delete, $"/api/tasks/{task}", anaToken), HttpStatusCode.NoContent);

        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(0, "", 0, 0, 0, 0, "null"));
    }

    [Fact]
    public async Task ADeadline_MovedBeforeTheCompletion_TurnsTheTaskLate()
    {
        var (ana, anaToken, _, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Pagina de start", [ana.Id], DateTimeOffset.UtcNow.AddDays(1));
        await Move(anaToken, task, DoneColumnOf(rep));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 1, 0, "1"));

        await Expect(
            await Send(HttpMethod.Put, $"/api/tasks/{task}/due-date", anaToken, new { dueDate = DateTimeOffset.UtcNow.AddHours(-2) }),
            HttpStatusCode.OK);

        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 0, 1, "0"));
    }

    [Fact]
    public async Task AnOpenTask_PastItsDeadlineThenFinished_LeavesTheOverdueForTheLate()
    {
        var (ana, anaToken, _, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Notificări", [ana.Id], DateTimeOffset.UtcNow.AddDays(1));
        (await NumbersOf(anaToken, rep)).Overdue.Should().Be(0);

        await Expect(
            await Send(HttpMethod.Put, $"/api/tasks/{task}/due-date", anaToken, new { dueDate = DateTimeOffset.UtcNow.AddHours(-1) }),
            HttpStatusCode.OK);
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(0, "", 0, 1, 0, 0, "null"));

        await Move(anaToken, task, DoneColumnOf(rep));
        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 0, 1, "0"));
    }

    // Each assignee counts the task; taking one off takes their count away (SPEC section 5.1.1).
    [Fact]
    public async Task ATaskFinished_WithTwoAssignees_CountsForEachUntilOneIsTakenOff()
    {
        var (ana, anaToken, dan, _, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Formular", [ana.Id, dan.Id], null);
        await Move(anaToken, task, DoneColumnOf(rep));
        (await NumbersOf(anaToken, rep)).ByUser.Should().Be("1 1");

        await Expect(await Send(HttpMethod.Put, $"/api/tasks/{task}/assignees", anaToken, new { assigneeIds = new[] { dan.Id } }), HttpStatusCode.OK);

        (await NumbersOf(anaToken, rep)).Should().Be(new Numbers(1, "1", 1, 0, 0, 0, "null"));
    }

    // The same numbers for reports.view, and none for a plain member of the project.
    [Fact]
    public async Task TheSameWork_ReadByReportsViewAndByAMember_IsTheOwnersOrRefused()
    {
        var (ana, anaToken, _, danToken, rep) = await ATeam();
        var task = await CreateTask(anaToken, rep, "Raport", [ana.Id], DateTimeOffset.UtcNow.AddDays(1));
        await Move(anaToken, task, DoneColumnOf(rep));

        (await NumbersOf(await TokenOf(await Person("Radu Rapoarte", Permissions.ReportsView)), rep)).Should().Be(await NumbersOf(anaToken, rep));
        var refused = await Send(HttpMethod.Get, $"/api/reports/completed?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}&projectId={rep.Id}", danToken);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeFrom(refused)).Should().Be("Project.OwnerRequired");
    }
}
