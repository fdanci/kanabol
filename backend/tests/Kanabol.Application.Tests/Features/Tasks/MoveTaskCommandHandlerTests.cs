using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddBoardColumn;
using Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// Moving a task, on the board or from its panel (SPEC section 6.3): to a place in a column of its
/// project, the others moving up or down so each column stays 0, 1, 2... A move that crosses another
/// at the same moment gets 409 from the versions of the tasks both changed (SPEC section 5.3). A task in
/// În lucru or after it never goes back before it (409 Task.CannotMoveBack, SPEC section 4.4). Most tests
/// move between Backlog and Analiză, a column of the project's own before În lucru, where nothing is asked.
/// </summary>
public sealed class MoveTaskCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Guid _backlog;
    private Guid _analysis;

    private Task<Result<TaskItemDto>> Move(Guid taskId, Guid columnId, int? position = null) =>
        Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(taskId, columnId, position));

    private static Guid InProgress(Project project) => project.Columns.Single(column => column.Kind == ColumnKind.InProgress).Id;

    private async Task<Guid> ColumnNamed(Project project, string name)
    {
        await using var db = Postgres.CreateContext();
        return await db.BoardColumns.Where(column => column.ProjectId == project.Id && column.Name == name).Select(column => column.Id).SingleAsync();
    }

    /// <summary>The titles in each column, as stored, in their order, and whether the places are 0, 1, 2...</summary>
    private async Task<string> Board(Project project)
    {
        await using var db = Postgres.CreateContext();
        var tasks = await db.TaskItems.Where(task => task.ProjectId == project.Id).ToListAsync();
        var columns = await db.BoardColumns.Where(column => column.ProjectId == project.Id).OrderBy(column => column.Position).ToListAsync();
        return string.Join(
            " | ",
            columns.Select(column =>
            {
                var inColumn = tasks.Where(task => task.ColumnId == column.Id).OrderBy(task => task.Position).ToList();
                inColumn.Select(task => task.Position).Should().Equal(Enumerable.Range(0, inColumn.Count), $"the places in {column.Name}");
                return string.Join(' ', inColumn.Select(task => task.Title));
            }));
    }

    /// <summary>
    /// A, B and C in Backlog, D and E in Analiză, the project's own column right after it, nothing in În lucru,
    /// În pauză or Gata. Every task starts in Backlog, so D and E move to Analiză.
    /// </summary>
    private async Task<(User Owner, Project Project, Dictionary<string, TaskItemDto> Tasks)> ABoard()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        SignIn(owner);
        _backlog = project.Columns.Single(column => column.Kind == ColumnKind.Backlog).Id;
        _analysis = (await Handle<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, "Analiză"))).Value.Id;
        (await Handle<MoveBoardColumnCommand, BoardColumnDto[]>(new MoveBoardColumnCommand(project.Id, _analysis, 1))).IsSuccess.Should().BeTrue();
        var tasks = new Dictionary<string, TaskItemDto>();
        foreach (var title in new[] { "A", "B", "C", "D", "E" })
        {
            tasks[title] = (await Create(NewTask(project, title))).Value;
        }

        foreach (var title in new[] { "D", "E" })
        {
            (await Move(tasks[title].Id, _analysis)).IsSuccess.Should().BeTrue();
        }

        (await Board(project)).Should().Be("A B C | D E |  |  | ");
        return (owner, project, tasks);
    }

    [Fact]
    public async Task Move_ToAPlaceInAnotherColumn_PutsItThereAndCloseTheGapItLeft()
    {
        var (_, project, tasks) = await ABoard();

        var result = await Move(tasks["A"].Id, _analysis, 1);

        result.Value.Should().BeEquivalentTo(new { ColumnId = _analysis, ColumnName = "Analiză", Position = 1 });
        (await Board(project)).Should().Be("B C | D A E |  |  | ");
    }

    [Theory]
    [InlineData("A", 2, "B C A")]
    [InlineData("C", 0, "C A B")]
    [InlineData("B", 0, "B A C")]
    [InlineData("A", 1, "B A C")]
    [InlineData("B", 1, "A B C")]
    public async Task Move_WithinItsColumn_ReordersIt(string title, int position, string order)
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks[title].Id, _backlog, position)).Value.Position.Should().Be(position);

        (await Board(project)).Should().Be($"{order} | D E |  |  | ");
    }

    [Fact]
    public async Task Move_WithoutAPlace_PutsItAtTheEnd()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["A"].Id, _analysis)).Value.Position.Should().Be(2);

        (await Board(project)).Should().Be("B C | D E A |  |  | ");
    }

    [Fact]
    public async Task Move_PastTheEnd_PutsItAtTheEnd()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["E"].Id, _backlog, 99)).Value.Position.Should().Be(3);

        (await Board(project)).Should().Be("A B C E | D |  |  | ");
    }

    // Out of Gata a task goes to În lucru or after it, and is open again (SPEC section 4.4).
    [Fact]
    public async Task Move_IntoTheDoneColumn_CompletesItAndOutClearsThat()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["A"].Id, DoneColumn(project).Id)).Value.CompletedAt.Should().Be(Now);
        (await Move(tasks["A"].Id, InProgress(project), 0)).Value.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Move_ToAnotherColumn_WritesTheColumnsInTheHistoryAndAReorderDoesNot()
    {
        var (owner, _, tasks) = await ABoard();

        await Move(tasks["A"].Id, _analysis, 0);
        await Move(tasks["A"].Id, _analysis, 2);

        (await Stored(tasks["A"].Id))!.History.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Field = "ColumnId",
            OldValue = _backlog.ToString(),
            NewValue = _analysis.ToString(),
            ChangedById = owner.Id,
        });
        (await Stored(tasks["B"].Id))!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_PastAnArchivedTask_GivesItNoPlace()
    {
        var (_, project, tasks) = await ABoard();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(tasks["B"].Id))).IsSuccess.Should().BeTrue();

        (await Move(tasks["D"].Id, _backlog)).Value.Position.Should().Be(2);

        (await Board(project)).Should().Be("A C D | E |  |  | ");
    }

    [Fact]
    public async Task Move_ToAColumnOfAnotherProject_ReturnsAValidationErrorOnColumnIdAndMovesNothing()
    {
        var (owner, project, tasks) = await ABoard();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        var result = await Move(tasks["A"].Id, ColumnAt(other, 0).Id, 0);

        result.Error.Should().Be(TaskErrors.ColumnNotInProject);
        (await Board(project)).Should().Be("A B C | D E |  |  | ");
    }

    [Fact]
    public async Task Move_ToANegativePlace_ReturnsAValidationErrorOnPosition()
    {
        var (_, _, tasks) = await ABoard();

        (await Move(tasks["A"].Id, _analysis, -1)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Position");
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Move_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired(string permission)
    {
        var (_, _, tasks) = await ABoard();
        await SignInWith(permission);

        (await Move(tasks["A"].Id, _analysis, 0)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Move_InAnArchivedProject_ReturnsArchived()
    {
        var (owner, project, tasks) = await ABoard();
        await Archive(project, owner);

        (await Move(tasks["A"].Id, _analysis, 0)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Move_OfAnArchivedTask_ReturnsNotFound()
    {
        var (_, _, tasks) = await ABoard();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(tasks["A"].Id))).IsSuccess.Should().BeTrue();

        (await Move(tasks["A"].Id, _analysis, 0)).Error.Should().Be(TaskErrors.NotFound);
    }

    // SPEC section 4.4: a task in În lucru or after it never goes back before În lucru, wherever it was before.
    [Theory]
    [InlineData("În lucru", "Backlog")]
    [InlineData("În lucru", "Analiză")]
    [InlineData("În pauză", "Backlog")]
    [InlineData("Gata", "Backlog")]
    [InlineData("Gata", "Analiză")]
    public async Task Move_FromInProgressOrAfterToAColumnBeforeIt_Returns409CannotMoveBackAndMovesNothing(string from, string to)
    {
        var (_, project, tasks) = await ABoard();
        (await Move(tasks["A"].Id, await ColumnNamed(project, from))).IsSuccess.Should().BeTrue();
        var before = await Board(project);

        var result = await Move(tasks["A"].Id, await ColumnNamed(project, to), 0);

        result.Error.Should().Be(TaskErrors.CannotMoveBack);
        (await Board(project)).Should().Be(before);
        (await Stored(tasks["A"].Id))!.History.Should().ContainSingle("only the move into the column it's in");
    }

    [Theory]
    [InlineData("În lucru", "În pauză")]
    [InlineData("În pauză", "În lucru")]
    [InlineData("Gata", "În lucru")]
    [InlineData("Gata", "În pauză")]
    [InlineData("În lucru", "În lucru")]
    public async Task Move_FromInProgressOrAfterToItOrAColumnAfterIt_IsAllowed(string from, string to)
    {
        var (_, project, tasks) = await ABoard();
        (await Move(tasks["A"].Id, await ColumnNamed(project, from))).IsSuccess.Should().BeTrue();

        var result = await Move(tasks["A"].Id, await ColumnNamed(project, to), 0);

        result.Value.ColumnName.Should().Be(to);
        result.Value.CompletedAt.Should().BeNull();
    }

    // The rule is on where the task is now: before În lucru it moves freely, both ways.
    [Fact]
    public async Task Move_BetweenColumnsBeforeInProgress_IsAllowedBothWays()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["D"].Id, _backlog, 0)).IsSuccess.Should().BeTrue();
        (await Move(tasks["D"].Id, _analysis, 0)).IsSuccess.Should().BeTrue();

        (await Board(project)).Should().Be("A B C | D E |  |  | ");
    }

    // SPEC section 4.4, the order: the request's form, the access, then Task.CannotMoveBack.
    [Fact]
    public async Task Move_BackWithoutAccessOrToAColumnNotOnTheBoard_SaysThatFirst()
    {
        var (owner, project, tasks) = await ABoard();
        (await Move(tasks["A"].Id, InProgress(project))).IsSuccess.Should().BeTrue();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        (await Move(tasks["A"].Id, _backlog, -1)).Error.Should().BeOfType<ValidationError>();
        (await Move(tasks["A"].Id, ColumnAt(other, 0).Id, 0)).Error.Should().Be(TaskErrors.ColumnNotInProject);
        await SignInWith(Permissions.ProjectsManage);
        (await Move(tasks["A"].Id, _backlog, 0)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    // SPEC section 5.3: two moves through the same column at once; the second changes nothing.
    [Fact]
    public async Task Move_TwoAtOnceThroughTheSameColumn_GiveTheSecondChangedMeanwhile()
    {
        var (owner, project, tasks) = await ABoard();

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(tasks["C"].Id, _backlog, 0), owner),
            async () => (await HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(tasks["A"].Id, _analysis, 0), owner)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        (await Board(project)).Should().Be("B C | A D E |  |  | ");
    }

    // Two moves at once that change different tasks both save, and can leave two tasks on one place.
    [Fact]
    public async Task Move_IntoAColumnWithTwoTasksOnOnePlace_OrdersThemByNumberAndMendsThePlaces()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var builder = () => new Kanabol.Tests.Shared.Builders.TaskItemBuilder().InProject(project);
        await AddTasks(project, builder().WithTitle("Unu").Build(), builder().WithTitle("Doi").Build(), builder().WithTitle("Trei").Build());
        SignIn(owner);
        var moved = (await Create(NewTask(project, "Nou"))).Value;

        await Move(moved.Id, project.Columns.Single(column => column.Kind == ColumnKind.Backlog).Id, 1);

        (await Board(project)).Should().Be("Unu Nou Doi Trei |  |  | ");
    }

    [Fact]
    public async Task Move_ToTheSamePlace_ChangesNothing()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["B"].Id, _backlog, 1)).Value.Position.Should().Be(1);

        (await Board(project)).Should().Be("A B C | D E |  |  | ");
        (await Stored(tasks["B"].Id))!.UpdatedAt.Should().BeNull();
    }
}
