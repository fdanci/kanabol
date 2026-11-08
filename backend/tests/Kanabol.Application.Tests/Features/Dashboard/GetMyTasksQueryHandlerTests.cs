using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Queries.GetMyTasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Dashboard;

/// <summary>
/// The caller's own tasks across every project (Tasks.md group 17a; SPEC section 4.4, "Panoul principal și
/// task-urile mele"; UI spec section 7.2): the project list's filters and sorts but the assignee, plus open,
/// by deadline from the soonest unless asked otherwise, those without one last, then by key from the newest.
/// </summary>
public sealed class GetMyTasksQueryHandlerTests(PostgresFixture postgres) : DashboardHandlerTest(postgres)
{
    private Task<Result<Page<MyTaskDto>>> List(Func<GetMyTasksQuery, GetMyTasksQuery>? configure = null) =>
        Ask<GetMyTasksQuery, Page<MyTaskDto>>((configure ?? (query => query))(new GetMyTasksQuery()));

    private static string KeysOf(Result<Page<MyTaskDto>> result) =>
        result.IsSuccess ? string.Join(' ', result.Value.Items.Select(task => task.Key)) : result.Error.ToString();

    [Fact]
    public async Task List_ByDefault_GivesTheCallersOwnByDeadlineThenByKeyFromTheNewest()
    {
        await DansTasks();

        var result = await List();

        // Not KAN-3 or WEB-2 (archived), OLD-1 (an archived project), MOB-1 (a project Dan left) or KAN-6 (Ana's).
        KeysOf(result).Should().Be("KAN-5 KAN-1 KAN-2 WEB-1 KAN-4");
        result.Value.Should().BeEquivalentTo(new { Number = 1, PageSize = 50, TotalCount = 5 });
    }

    [Fact]
    public async Task List_OfOpenOnes_LeavesTheFinishedOut()
    {
        await DansTasks();

        KeysOf(await List(query => query with { Open = true })).Should().Be("KAN-1 KAN-2 WEB-1");
    }

    [Fact]
    public async Task List_Always_GivesEachRowItsProjectBesideWhatTheProjectListGives()
    {
        var team = await DansTasks();

        var row = (await List()).Value.Items.Single(task => task.Key == "WEB-1");

        row.Should().BeEquivalentTo(new
        {
            ProjectId = team.Web.Id,
            ProjectKey = "WEB",
            ProjectName = "Site",
            Number = 1,
            Title = "Formular de contact",
            ColumnId = ColumnAt(team.Web, 0).Id,
            ColumnName = "De făcut",
            ColumnIsDone = false,
            Priority = TaskPriority.Critical,
            DueDate = (DateTimeOffset?)null,
            Assignees = new[] { new TaskPersonDto(team.Ana.Id, "Ana Pop"), new TaskPersonDto(team.Dan.Id, "Dan Ionescu") },
            CompletedAt = (DateTimeOffset?)null,
            ArchivedAt = (DateTimeOffset?)null,
        });
    }

    [Fact]
    public async Task List_WithIncludeArchivedAndTasksArchive_GivesTheArchivedTasksButNotThoseOfAnArchivedProject()
    {
        await DansTasks();
        HoldsInToken(Permissions.TasksArchive);

        KeysOf(await List(query => query with { IncludeArchived = true })).Should().Be("KAN-5 KAN-3 KAN-1 KAN-2 WEB-2 WEB-1 KAN-4");
    }

    [Fact]
    public async Task List_WithIncludeArchivedWithoutTasksArchive_ReturnsPermissionRequired()
    {
        await DansTasks();

        (await List(query => query with { IncludeArchived = true })).Error.Should().Be(AuthErrors.PermissionRequired);
    }

    // Overdue as in the project list: open past the deadline, outside a done column, so not KAN-5.
    [Fact]
    public async Task List_OfOverdueOnes_GivesTheOpenTasksPastTheirDeadline()
    {
        await DansTasks();

        KeysOf(await List(query => query with { Overdue = true })).Should().Be("KAN-1");
    }

    [Fact]
    public async Task List_OfPriorities_GivesThoseWithAnyOfThemInEveryProject()
    {
        await DansTasks();

        KeysOf(await List(query => query with { Priorities = [TaskPriority.High, TaskPriority.Critical] })).Should().Be("KAN-1 WEB-1");
    }

    [Fact]
    public async Task List_WithASearch_GivesTheTitlesThatHaveIt()
    {
        await DansTasks();

        KeysOf(await List(query => query with { Search = "LO" })).Should().Be("KAN-1 KAN-4");
    }

    [Fact]
    public async Task List_WithFiltersTogether_GivesTasksThatPassThemAll()
    {
        await DansTasks();

        KeysOf(await List(query => query with { Open = true, Priorities = [TaskPriority.Critical, TaskPriority.Low] })).Should().Be("KAN-2 WEB-1");
    }

    // The key across projects is the project's key, then the number; ties go by key from the newest,
    // whichever the direction, as the default's do.
    [Theory]
    [InlineData("key:asc", "KAN-1 KAN-2 KAN-4 KAN-5 WEB-1")]
    [InlineData("key:desc", "WEB-1 KAN-5 KAN-4 KAN-2 KAN-1")]
    [InlineData("title:asc", "WEB-1 KAN-1 KAN-4 KAN-2 KAN-5")]
    [InlineData("dueDate:asc", "KAN-5 KAN-1 KAN-2 WEB-1 KAN-4")]
    [InlineData("dueDate:desc", "KAN-2 KAN-1 KAN-5 WEB-1 KAN-4")]
    [InlineData("priority:desc", "WEB-1 KAN-1 KAN-5 KAN-4 KAN-2")]
    [InlineData("priority:asc", "KAN-2 KAN-5 KAN-4 KAN-1 WEB-1")]
    public async Task List_SortedBy_GivesThemInThatOrder(string sort, string keys)
    {
        await DansTasks();

        KeysOf(await List(query => query with { Sort = sort })).Should().Be(keys);
    }

    [Fact]
    public async Task List_APageAtATime_GivesThatPage()
    {
        await DansTasks();

        var result = await List(query => query with { Page = 2, PageSize = 2 });

        KeysOf(result).Should().Be("KAN-2 WEB-1");
        result.Value.TotalCount.Should().Be(5);
    }

    // SPEC section 4.4: projects.view_all doesn't widen anything here.
    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task List_WithAPermissionOverAllProjects_GivesNothingOfOthers(string permission)
    {
        await DansTasks();
        await SignInWith(permission);

        (await List()).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_ForADeactivatedUser_GivesNothing()
    {
        var team = await DansTasks();
        await Deactivate(team.Dan);

        (await List()).Value.TotalCount.Should().Be(0);
    }

    [Theory]
    [InlineData("color:asc")]
    [InlineData("dueDate")]
    [InlineData("")]
    public async Task List_SortedByWhatIsNotAColumn_ReturnsAValidationErrorOnSort(string sort)
    {
        await DansTasks();

        (await List(query => query with { Sort = sort })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Sort");
    }

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(1, 101, "PageSize")]
    public async Task List_WithAPageOutOfRange_ReturnsAValidationError(int page, int pageSize, string field)
    {
        await DansTasks();

        (await List(query => query with { Page = page, PageSize = pageSize })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task List_WithASearchOverTheLimit_ReturnsAValidationErrorOnSearch()
    {
        await DansTasks();

        (await List(query => query with { Search = new string('a', 301) })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Search");
    }
}
