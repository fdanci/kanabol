using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Queries.GetProjects;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>
/// The list of projects (SPEC section 4.4, UI spec section 7.3): a member sees theirs, projects.view_all
/// or projects.manage see every one, and each row has what the table shows.
/// </summary>
public sealed class GetProjectsQueryHandlerTests(PostgresFixture postgres) : ProjectHandlerTest(postgres)
{
    private Task<Result<Page<ProjectSummaryDto>>> List(int page = 1, int pageSize = 50, bool includeArchived = false, bool mine = false) =>
        Ask<GetProjectsQuery, Page<ProjectSummaryDto>>(new GetProjectsQuery(page, pageSize, includeArchived, mine));

    [Fact]
    public async Task Handle_ForAMember_ListsOnlyTheirProjects()
    {
        var user = await CreateUser();
        var owned = await CreateProject(builder => builder.WithName("Al meu").OwnedBy(user));
        var joined = await CreateProject(builder => builder.WithName("Altul").WithMember(user));
        await CreateProject(builder => builder.WithName("Străin"));
        SignIn(user);

        var result = await List();

        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(project => (project.Id, project.MyRole)).Should().Equal((owned.Id, ProjectRole.Owner), (joined.Id, ProjectRole.Member));
    }

    [Fact]
    public async Task Handle_ForSomeoneInNoProject_ReturnsAnEmptyPage()
    {
        await CreateProject();
        var user = await CreateUser("outsider@example.com");
        SignIn(user);

        var result = await List();

        result.Value.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Handle_WithAPermissionOverAllProjects_ListsEveryOne(string permission)
    {
        await CreateProject(builder => builder.WithName("Unu"));
        await CreateProject(builder => builder.WithName("Doi"));
        await SignInWith(permission);

        var result = await List();

        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Should().OnlyContain(project => project.MyRole == null);
    }

    [Fact]
    public async Task Handle_ForADeactivatedUser_ListsNothing()
    {
        var user = await CreateUser(permissions: [Permissions.ProjectsViewAll]);
        await CreateProject(builder => builder.OwnedBy(user));
        await Deactivate(user);
        SignIn(user);

        (await List()).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_ByDefault_LeavesOutArchivedProjects()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.WithName("Deschis").OwnedBy(user));
        var archived = await CreateProject(builder => builder.WithName("Arhivat").OwnedBy(user));
        await Archive(archived, user);
        SignIn(user);

        (await List()).Value.Items.Select(project => project.Name).Should().Equal("Deschis");
    }

    [Fact]
    public async Task Handle_WithIncludeArchived_ListsThemWithWhen()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.WithName("Deschis").OwnedBy(user));
        var archived = await CreateProject(builder => builder.WithName("Arhivat").OwnedBy(user));
        await Archive(archived, user);
        var foreign = await CreateProject(builder => builder.WithName("Străin arhivat"));
        await Archive(foreign, user);
        SignIn(user);

        var result = await List(includeArchived: true);

        result.Value.Items.Select(project => (project.Name, project.ArchivedAt)).Should().Equal(("Arhivat", Now), ("Deschis", null));
    }

    [Fact]
    public async Task Handle_Always_SortsByNameAndPages()
    {
        var user = await CreateUser();
        foreach (var name in new[] { "Delta", "alfa", "Charlie", "Bravo", "Echo" })
        {
            await CreateProject(builder => builder.WithName(name).WithMember(user));
        }

        SignIn(user);

        var result = await List(page: 2, pageSize: 2);

        result.Value.Should().BeEquivalentTo(new { Number = 2, PageSize = 2, TotalCount = 5 });
        result.Value.Items.Select(project => project.Name).Should().Equal("Charlie", "Delta");
    }

    [Fact]
    public async Task Handle_Always_GivesTheOwnersAndUpToFourMembersOwnersFirst()
    {
        var owner = await CreateUser("owner@example.com");
        var members = new List<Kanabol.Domain.Users.User>();
        for (var index = 0; index < 5; index++)
        {
            members.Add(await CreateUser($"member{index}@example.com"));
        }

        await CreateProject(builder => members.Aggregate(builder.OwnedBy(owner), (next, member) => next.WithMember(member)));
        SignIn(owner);

        var project = (await List()).Value.Items.Single();

        project.Owners.Should().ContainSingle().Which.Should().BeEquivalentTo(new { UserId = owner.Id, owner.DisplayName });
        project.MemberCount.Should().Be(6);
        project.MemberPreview.Should().HaveCount(4);
        project.MemberPreview[0].UserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task Handle_Always_CountsOpenAndOverdueTasksButNotDoneOrArchivedOnes()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var todo = project.Columns.Single(column => column.Position == 0);
        var done = project.Columns.Single(column => column.Kind == ColumnKind.Done);
        var archived = new TaskItemBuilder().InProject(project).InColumn(todo).DueAt(Now.AddDays(-1)).Build();
        await AddTasks(
            new TaskItemBuilder().InProject(project).InColumn(todo).Build(),
            new TaskItemBuilder().InProject(project).InColumn(todo).DueAt(Now.AddDays(-1)).Build(),
            new TaskItemBuilder().InProject(project).InColumn(todo).DueAt(Now.AddDays(1)).Build(),
            new TaskItemBuilder().InProject(project).InColumn(done).DueAt(Now.AddDays(-1)).Build(),
            archived);
        await ArchiveTask(archived);
        SignIn(owner);

        var summary = (await List()).Value.Items.Single();

        summary.OpenTaskCount.Should().Be(3);
        summary.OverdueTaskCount.Should().Be(1);
    }

    // SPEC section 4.4 (group 17a): the progress bar is finished / (open + finished), archived tasks out.
    [Fact]
    public async Task Handle_Always_CountsTheFinishedTasksButNotArchivedOnes()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var done = project.Columns.Single(column => column.Kind == ColumnKind.Done);
        var archived = new TaskItemBuilder().InProject(project).InColumn(done).Build();
        await AddTasks(
            new TaskItemBuilder().InProject(project).Build(),
            new TaskItemBuilder().InProject(project).InColumn(done).Build(),
            new TaskItemBuilder().InProject(project).InColumn(done).Build(),
            archived);
        await ArchiveTask(archived);
        SignIn(owner);

        var summary = (await List()).Value.Items.Single();

        (summary.OpenTaskCount, summary.CompletedTaskCount).Should().Be((1, 2));

        // includeArchived turns the tasks' own filter off too.
        (await List(includeArchived: true)).Value.Items.Single().CompletedTaskCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OfAProjectWithoutTasks_CountsNoneFinished()
    {
        var owner = await CreateUser();
        await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await List()).Value.Items.Single().CompletedTaskCount.Should().Be(0);
    }

    // SPEC section 4.4 (group 17a): mine=true is the projects the caller is a member of, not those
    // projects.view_all or projects.manage show.
    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Handle_OfMineWithAPermissionOverAllProjects_ListsOnlyThoseTheCallerIsAMemberOf(string permission)
    {
        var manager = await SignInWith(permission);
        var joined = await CreateProject(builder => builder.WithName("Al meu").WithMember(manager));
        await CreateProject(builder => builder.WithName("Altul"));

        var result = await List(mine: true);

        result.Value.Items.Select(project => (project.Id, project.MyRole)).Should().Equal((joined.Id, ProjectRole.Member));
        result.Value.TotalCount.Should().Be(1);
        (await List()).Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OfMineForAMember_ListsTheirProjectsAsWithout()
    {
        var user = await CreateUser();
        var owned = await CreateProject(builder => builder.WithName("Al meu").OwnedBy(user));
        await CreateProject(builder => builder.WithName("Străin"));
        SignIn(user);

        (await List(mine: true)).Value.Items.Select(project => project.Id).Should().Equal(owned.Id);
    }

    [Fact]
    public async Task Handle_OfMine_LeavesOutArchivedProjectsUnlessAskedFor()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.WithName("Deschis").OwnedBy(user));
        var closed = await CreateProject(builder => builder.WithName("Închis").OwnedBy(user));
        await Archive(closed, user);
        SignIn(user);

        (await List(mine: true)).Value.TotalCount.Should().Be(1);
        (await List(includeArchived: true, mine: true)).Value.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_OfMineForADeactivatedUser_ListsNothing()
    {
        var user = await CreateUser();
        await CreateProject(builder => builder.OwnedBy(user));
        await Deactivate(user);
        SignIn(user);

        (await List(mine: true)).Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithIncludeArchived_StillLeavesArchivedTasksOutOfTheCounts()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var archived = new TaskItemBuilder().InProject(project).Build();
        await AddTasks(new TaskItemBuilder().InProject(project).Build(), archived);
        await ArchiveTask(archived);
        SignIn(owner);

        (await List(includeArchived: true)).Value.Items.Single().OpenTaskCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_Always_GivesTheLastChangeOfTheProjectOrItsTasks()
    {
        var owner = await CreateUser();
        var quiet = await CreateProject(builder => builder.WithName("Liniștit").OwnedBy(owner));
        var busy = await CreateProject(builder => builder.WithName("Ocupat").OwnedBy(owner));
        var later = Now.AddHours(3);
        var task = new TaskItemBuilder().InProject(busy).Build();
        task.CreatedAt = Now.AddHours(1);
        task.UpdatedAt = later;
        await AddTasks(task, new TaskItemBuilder().InProject(busy).Build());
        SignIn(owner);

        var items = (await List()).Value.Items;

        items.Single(project => project.Id == quiet.Id).UpdatedAt.Should().Be(Now);
        items.Single(project => project.Id == busy.Id).UpdatedAt.Should().Be(later);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Handle_WhenThePageIsOutOfRange_ReturnsAValidationError(int page, int pageSize)
    {
        await SignInWith(Permissions.ProjectsViewAll);

        (await List(page, pageSize)).Error.Should().BeOfType<ValidationError>();
    }

    private async Task AddTasks(params TaskItem[] tasks)
    {
        await using var db = Postgres.CreateContext();
        db.TaskItems.AddRange(tasks);
        await db.SaveChangesAsync();
    }

    private async Task ArchiveTask(TaskItem task)
    {
        await using var db = Postgres.CreateContext();
        await db.TaskItems.Where(stored => stored.Id == task.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.ArchivedAt, Now));
    }
}
