using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Queries.GetBoard;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The board of a project (UI spec section 7.4): its columns from the left and every open task, in its
/// place, however many there are, since the board shows them all and filters them in the browser.
/// </summary>
public sealed class GetBoardQueryHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<BoardDto>> Board(Guid projectId) => Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(projectId));

    [Fact]
    public async Task Board_ForAnOwner_GivesTheColumnsAndTheOpenTasksInTheirPlaces()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.WithKey("BRD").OwnedBy(owner).WithMember(member));
        var first = await CreateTaskAs(owner, project, "Primul", assigneeIds: [member.Id]);
        await CreateTaskAs(owner, project, "Al doilea");
        var archived = await CreateTaskAs(owner, project, "Arhivat");
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(archived.Id))).IsSuccess.Should().BeTrue();

        var board = (await Board(project.Id)).Value;

        board.Should().BeEquivalentTo(new { ProjectId = project.Id, ProjectKey = "BRD", IsArchived = false, CanMove = true, CanManage = true });
        board.Columns.Select(column => (column.Name, column.Position, column.Kind)).Should().Equal(
            ("Backlog", 0, ColumnKind.Backlog), ("În lucru", 1, ColumnKind.InProgress), ("În pauză", 2, ColumnKind.Paused), ("Gata", 3, ColumnKind.Done));
        board.Tasks.Select(task => (task.Key, task.Title, task.ColumnId, task.Position)).Should().Equal(
            ("BRD-1", "Primul", ColumnAt(project, 0).Id, 0),
            ("BRD-2", "Al doilea", ColumnAt(project, 0).Id, 1));
        board.Tasks[0].Should().BeEquivalentTo(new
        {
            first.Id,
            Number = 1,
            Priority = TaskPriority.Medium,
            DueDate = (DateTimeOffset?)null,
            Assignees = new[] { new TaskPersonDto(member.Id, member.DisplayName) },
            CompletedAt = (DateTimeOffset?)null,
            CommentCount = 0,
            AttachmentCount = 0,
        });
    }

    // UI spec section 7.4: the card shows every assignee, by name.
    [Fact]
    public async Task Board_OfATaskWithSeveralAssignees_GivesThemAllByName()
    {
        var owner = await CreateUserNamed("Zoe Ene", "zoe@example.com");
        var ana = await CreateUserNamed("Ana Pop", "ana.pop@example.com");
        var project = await CreateProject(builder => builder.WithKey("BRD").OwnedBy(owner).WithMember(ana));
        await CreateTaskAs(owner, project, assigneeIds: [owner.Id, ana.Id]);

        var card = (await Board(project.Id)).Value.Tasks.Single();

        card.Assignees.Should().Equal(new TaskPersonDto(ana.Id, "Ana Pop"), new TaskPersonDto(owner.Id, "Zoe Ene"));
    }

    // The list comes in pages of at most 100; the board has no pages.
    [Fact]
    public async Task Board_WithMoreThanAPageOfTasks_GivesThemAll()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await AddTasks(project, [.. Enumerable.Range(0, 120).Select(position => new TaskItemBuilder().InProject(project).AtPosition(position).Build())]);
        SignIn(owner);

        (await Board(project.Id)).Value.Tasks.Should().HaveCount(120).And.BeInAscendingOrder(task => task.Position);
    }

    [Fact]
    public async Task Board_ForAMember_CanMoveButNotManage()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        (await Board(project.Id)).Value.Should().BeEquivalentTo(new { CanMove = true, CanManage = false });
    }

    // SPEC section 4.4: projects.view_all and projects.manage read; only a real member moves tasks.
    [Theory]
    [InlineData(Permissions.ProjectsViewAll, false)]
    [InlineData(Permissions.ProjectsManage, true)]
    public async Task Board_ForOneWhoSeesItThroughAPermission_CannotMove(string permission, bool canManage)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(permission);

        (await Board(project.Id)).Value.Should().BeEquivalentTo(new { CanMove = false, CanManage = canManage });
    }

    [Fact]
    public async Task Board_OfAnArchivedProject_OpensButChangesNothing()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await Archive(project, owner);
        SignIn(owner);

        (await Board(project.Id)).Value.Should().BeEquivalentTo(new { IsArchived = true, CanMove = false, CanManage = false, CanManageColumns = false });
    }

    // SPEC section 4.4, group 23: canManageColumns for an owner, projects.manage or a real member with
    // columns.manage, read from the database; canManage stays the owner's and projects.manage's.
    [Fact]
    public async Task Board_ForAnOwner_CanManageTheColumns()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Board(project.Id)).Value.Should().BeEquivalentTo(new { CanManage = true, CanManageColumns = true });
    }

    [Theory]
    [InlineData(Permissions.ColumnsManage, true)]
    [InlineData(Permissions.TasksCreate, false)]
    public async Task Board_ForAMember_CanManageTheColumnsOnlyWithColumnsManage(string permission, bool canManageColumns)
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com", permissions: permission);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        (await Board(project.Id)).Value.Should().BeEquivalentTo(new { CanMove = true, CanManage = false, CanManageColumns = canManageColumns });
    }

    [Theory]
    [InlineData(new[] { Permissions.ProjectsManage }, true)]
    [InlineData(new[] { Permissions.ProjectsViewAll, Permissions.ColumnsManage }, false)]
    public async Task Board_ForOneWhoSeesItThroughAPermission_CanManageTheColumnsOnlyWithProjectsManage(string[] permissions, bool canManageColumns)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(permissions);

        (await Board(project.Id)).Value.CanManageColumns.Should().Be(canManageColumns);
    }

    [Fact]
    public async Task Board_OfAnArchivedProject_LetsNoMemberWithColumnsManageChangeTheColumns()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com", permissions: Permissions.ColumnsManage);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        await Archive(project, owner);
        SignIn(member);

        (await Board(project.Id)).Value.CanManageColumns.Should().BeFalse();
    }

    [Fact]
    public async Task Board_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(await CreateUser("dan@example.com"));

        (await Board(project.Id)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Board_OfAProjectThatDoesNotExist_ReturnsNotFound()
    {
        SignIn(await CreateUser());

        (await Board(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }
}
