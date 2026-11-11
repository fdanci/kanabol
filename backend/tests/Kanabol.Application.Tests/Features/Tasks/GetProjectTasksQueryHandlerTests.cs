using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The project's tasks as the List view shows them (UI spec section 7.3): a page at a time, by key from
/// the newest unless asked otherwise, with the board's filters, and archived ones only with
/// includeArchived and tasks.archive (SPEC section 5.1).
/// </summary>
public sealed class GetProjectTasksQueryHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<Page<TaskSummaryDto>>> List(Guid projectId, Func<GetProjectTasksQuery, GetProjectTasksQuery>? configure = null) =>
        Ask<GetProjectTasksQuery, Page<TaskSummaryDto>>((configure ?? (query => query))(new GetProjectTasksQuery(projectId)));

    private static IEnumerable<string> KeysOf(Result<Page<TaskSummaryDto>> result) => result.Value.Items.Select(task => task.Key);

    /// <summary>Five tasks of KAN, made in this order: the third archived, the fifth in the done column.</summary>
    private async Task<(User Owner, User Member, Project Project)> AProjectWithTasks()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member));
        var done = DoneColumn(project);
        var builder = () => new TaskItemBuilder().InProject(project).CreatedBy(owner);
        TaskItem[] tasks =
        [
            builder().WithTitle("Login SSO").WithPriority(TaskPriority.High).AssignedTo(member).DueAt(Now.AddDays(-1)).Build(),
            builder().WithTitle("Pagina de start").WithPriority(TaskPriority.Low).DueAt(Now.AddDays(5)).Build(),
            builder().WithTitle("Arhivat").DueAt(Now.AddDays(-2)).Build(),
            builder().WithTitle("Raport 100% gata").WithPriority(TaskPriority.Critical).AssignedTo(owner).AtPosition(1).Build(),
            builder().InColumn(done).WithTitle("Logo nou").WithPriority(TaskPriority.Medium).AssignedTo(member).DueAt(Now.AddDays(-3)).Build(),
        ];
        tasks[2].Archive(owner.Id, Now);
        await AddTasks(project, tasks);
        SignIn(member);
        return (owner, member, project);
    }

    [Fact]
    public async Task List_ByAMember_GivesTheOpenTasksByKeyFromTheNewest()
    {
        var (_, _, project) = await AProjectWithTasks();

        var result = await List(project.Id);

        KeysOf(result).Should().Equal("KAN-5", "KAN-4", "KAN-2", "KAN-1");
        result.Value.Should().BeEquivalentTo(new { Number = 1, PageSize = 50, TotalCount = 4 });
    }

    // Tasks.md group 12: archived tasks don't show by default.
    [Fact]
    public async Task List_ByDefault_LeavesTheArchivedOnesOut() =>
        KeysOf(await List((await AProjectWithTasks()).Project.Id)).Should().NotContain("KAN-3");

    [Fact]
    public async Task List_WithIncludeArchivedAndTasksArchive_GivesThemToo()
    {
        var (_, _, project) = await AProjectWithTasks();
        HoldsInToken(Permissions.TasksArchive);

        var result = await List(project.Id, query => query with { IncludeArchived = true });

        KeysOf(result).Should().Equal("KAN-5", "KAN-4", "KAN-3", "KAN-2", "KAN-1");
        result.Value.Items.Single(task => task.Key == "KAN-3").ArchivedAt.Should().Be(Now);
    }

    [Fact]
    public async Task List_WithIncludeArchivedWithoutTasksArchive_ReturnsPermissionRequired()
    {
        var (_, _, project) = await AProjectWithTasks();

        (await List(project.Id, query => query with { IncludeArchived = true })).Error.Should().Be(AuthErrors.PermissionRequired);
    }

    [Fact]
    public async Task List_Always_GivesEachRowItsColumnPriorityDeadlineAssigneeAndLastChange()
    {
        var (_, member, project) = await AProjectWithTasks();

        var row = (await List(project.Id)).Value.Items.Single(task => task.Key == "KAN-1");

        row.Should().BeEquivalentTo(new
        {
            Number = 1,
            Title = "Login SSO",
            ColumnId = ColumnAt(project, 0).Id,
            ColumnName = "Backlog",
            ColumnKind = ColumnKind.Backlog,
            Priority = TaskPriority.High,
            DueDate = Now.AddDays(-1),
            Assignees = new[] { new TaskPersonDto(member.Id, member.DisplayName) },
            CompletedAt = (DateTimeOffset?)null,
            ArchivedAt = (DateTimeOffset?)null,
        });
    }

    // UI spec section 7.3: the row lists every assignee, by name, whatever the case.
    [Fact]
    public async Task List_OfATaskWithSeveralAssignees_GivesThemAllByName()
    {
        var owner = await CreateUserNamed("mihai Radu", "mihai@example.com");
        var ana = await CreateUserNamed("Ana Pop", "ana.pop@example.com");
        var zoe = await CreateUserNamed("Zoe Ene", "zoe@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(ana).WithMember(zoe));
        await CreateTaskAs(owner, project, assigneeIds: [zoe.Id, owner.Id, ana.Id]);

        var row = (await List(project.Id)).Value.Items.Single();

        row.Assignees.Select(assignee => assignee.DisplayName).Should().Equal("Ana Pop", "mihai Radu", "Zoe Ene");
    }

    [Fact]
    public async Task List_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var (_, _, project) = await AProjectWithTasks();
        SignIn(await CreateUser("eva@example.com"));

        (await List(project.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task List_WithProjectsViewAll_GivesThem()
    {
        var (_, _, project) = await AProjectWithTasks();
        await SignInWith(Permissions.ProjectsViewAll);

        (await List(project.Id)).Value.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task List_OfAProjectThatDoesNotExist_ReturnsNotFound()
    {
        await AProjectWithTasks();

        (await List(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task List_OfAnArchivedProject_GivesItsTasks()
    {
        var (owner, _, project) = await AProjectWithTasks();
        await Archive(project, owner);

        (await List(project.Id)).Value.TotalCount.Should().Be(4);
    }

    // SPEC section 4.4: the filter matches a task that has them among its assignees.
    [Fact]
    public async Task List_OfAnAssignee_FindsTheTasksTheyShareWithOthers()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var other = await CreateUser("eva@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(member).WithMember(other));
        await CreateTaskAs(owner, project, "Ale amândurora", assigneeIds: [owner.Id, member.Id]);
        await CreateTaskAs(owner, project, "Al lui Dan", assigneeIds: [member.Id]);
        await CreateTaskAs(owner, project, "Al Evei", assigneeIds: [other.Id]);
        await CreateTaskAs(owner, project, "Al nimănui");

        KeysOf(await List(project.Id, query => query with { AssigneeId = member.Id })).Should().Equal("KAN-2", "KAN-1");
        KeysOf(await List(project.Id, query => query with { AssigneeId = owner.Id })).Should().Equal("KAN-1");
    }

    [Fact]
    public async Task List_OfAnAssignee_GivesOnlyTheirs()
    {
        var (_, member, project) = await AProjectWithTasks();

        KeysOf(await List(project.Id, query => query with { AssigneeId = member.Id })).Should().Equal("KAN-5", "KAN-1");
    }

    [Fact]
    public async Task List_OfPriorities_GivesThoseWithAnyOfThem()
    {
        var (_, _, project) = await AProjectWithTasks();

        KeysOf(await List(project.Id, query => query with { Priorities = [TaskPriority.High, TaskPriority.Critical] })).Should().Equal("KAN-4", "KAN-1");
    }

    // Overdue is open past the deadline, as in the project list: a task in the done column isn't.
    [Fact]
    public async Task List_OfOverdueOnes_GivesTheOpenTasksPastTheirDeadline()
    {
        var (_, _, project) = await AProjectWithTasks();

        KeysOf(await List(project.Id, query => query with { Overdue = true })).Should().Equal("KAN-1");
    }

    [Theory]
    [InlineData("login", "KAN-1")]
    [InlineData("PAGINA", "KAN-2")]
    [InlineData("100%", "KAN-4")]
    [InlineData("o_n", "")]
    [InlineData("%", "KAN-4")]
    public async Task List_WithASearch_GivesTheTitlesThatHaveIt(string search, string keys)
    {
        var (_, _, project) = await AProjectWithTasks();

        string.Join(' ', KeysOf(await List(project.Id, query => query with { Search = search }))).Should().Be(keys);
    }

    [Fact]
    public async Task List_WithFiltersTogether_GivesTasksThatPassThemAll()
    {
        var (_, member, project) = await AProjectWithTasks();

        KeysOf(await List(project.Id, query => query with { AssigneeId = member.Id, Priorities = [TaskPriority.Medium] })).Should().Equal("KAN-5");
    }

    [Theory]
    [InlineData("key:asc", "KAN-1 KAN-2 KAN-4 KAN-5")]
    [InlineData("title:asc", "KAN-1 KAN-5 KAN-2 KAN-4")]
    [InlineData("title:desc", "KAN-4 KAN-2 KAN-5 KAN-1")]
    [InlineData("priority:desc", "KAN-4 KAN-1 KAN-5 KAN-2")]
    [InlineData("priority:asc", "KAN-2 KAN-5 KAN-1 KAN-4")]
    [InlineData("dueDate:asc", "KAN-5 KAN-1 KAN-2 KAN-4")]
    [InlineData("dueDate:desc", "KAN-2 KAN-1 KAN-5 KAN-4")]
    [InlineData("column:desc", "KAN-5 KAN-4 KAN-2 KAN-1")]
    public async Task List_SortedBy_GivesThemInThatOrder(string sort, string keys)
    {
        var (_, _, project) = await AProjectWithTasks();

        string.Join(' ', KeysOf(await List(project.Id, query => query with { Sort = sort }))).Should().Be(keys);
    }

    // SPEC section 4.4: by the first name in alphabetical order, whatever the case, the unassigned last.
    [Theory]
    [InlineData("assignee:asc", "KAN-2 KAN-3 KAN-1 KAN-4")]
    [InlineData("assignee:desc", "KAN-1 KAN-3 KAN-2 KAN-4")]
    public async Task List_SortedByAssignee_GoesByTheFirstNameAlphabetically(string sort, string keys)
    {
        var owner = await CreateUserNamed("Zoe Ene", "zoe@example.com");
        var ana = await CreateUserNamed("ana Pop", "ana.pop@example.com");
        var mihai = await CreateUserNamed("Mihai Radu", "mihai@example.com");
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner).WithMember(ana).WithMember(mihai));
        await CreateTaskAs(owner, project, "Zoe", assigneeIds: [owner.Id]);
        await CreateTaskAs(owner, project, "Zoe și Ana", assigneeIds: [owner.Id, ana.Id]);
        await CreateTaskAs(owner, project, "Mihai și Zoe", assigneeIds: [mihai.Id, owner.Id]);
        await CreateTaskAs(owner, project, "Nimeni");

        string.Join(' ', KeysOf(await List(project.Id, query => query with { Sort = sort }))).Should().Be(keys);
    }

    [Fact]
    public async Task List_SortedByAssignee_PutsThemByNameAndTheUnassignedLast()
    {
        var (_, _, project) = await AProjectWithTasks();

        // The builder names everyone Ana Pop, so the tie goes by key, in the same direction.
        string.Join(' ', KeysOf(await List(project.Id, query => query with { Sort = "assignee:asc" }))).Should().Be("KAN-1 KAN-4 KAN-5 KAN-2");
    }

    [Theory]
    [InlineData("color:asc")]
    [InlineData("key")]
    [InlineData("key:up")]
    [InlineData("")]
    public async Task List_SortedByWhatIsNotAColumn_ReturnsAValidationErrorOnSort(string sort)
    {
        var (_, _, project) = await AProjectWithTasks();

        (await List(project.Id, query => query with { Sort = sort })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Sort");
    }

    [Fact]
    public async Task List_APageAtATime_GivesThatPage()
    {
        var (_, _, project) = await AProjectWithTasks();

        var result = await List(project.Id, query => query with { Page = 2, PageSize = 3 });

        KeysOf(result).Should().Equal("KAN-1");
        result.Value.TotalCount.Should().Be(4);
    }

    [Theory]
    [InlineData(0, 50, "Page")]
    [InlineData(1, 101, "PageSize")]
    public async Task List_WithAPageOutOfRange_ReturnsAValidationError(int page, int pageSize, string field)
    {
        var (_, _, project) = await AProjectWithTasks();

        (await List(project.Id, query => query with { Page = page, PageSize = pageSize })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey(field);
    }

    [Fact]
    public async Task List_WithASearchOverTheLimit_ReturnsAValidationErrorOnSearch()
    {
        var (_, _, project) = await AProjectWithTasks();

        (await List(project.Id, query => query with { Search = new string('a', 301) })).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Search");
    }
}
