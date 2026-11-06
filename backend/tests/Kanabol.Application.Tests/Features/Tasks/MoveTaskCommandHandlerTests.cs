using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
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
/// at the same moment gets 409 from the versions of the tasks both changed (SPEC section 5.3).
/// </summary>
public sealed class MoveTaskCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<TaskItemDto>> Move(Guid taskId, BoardColumn column, int? position = null) =>
        Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(taskId, column.Id, position));

    /// <summary>The titles in each column, in their order, and whether the places are 0, 1, 2...</summary>
    private async Task<string> Board(Project project)
    {
        await using var db = Postgres.CreateContext();
        var tasks = await db.TaskItems.Where(task => task.ProjectId == project.Id).ToListAsync();
        return string.Join(
            " | ",
            project.Columns.OrderBy(column => column.Position).Select(column =>
            {
                var inColumn = tasks.Where(task => task.ColumnId == column.Id).OrderBy(task => task.Position).ToList();
                inColumn.Select(task => task.Position).Should().Equal(Enumerable.Range(0, inColumn.Count), $"the places in {column.Name}");
                return string.Join(' ', inColumn.Select(task => task.Title));
            }));
    }

    /// <summary>A, B and C in De făcut, D and E in În lucru, nothing in Gata.</summary>
    private async Task<(User Owner, Project Project, Dictionary<string, TaskItemDto> Tasks)> ABoard()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("KAN").OwnedBy(owner));
        SignIn(owner);
        var tasks = new Dictionary<string, TaskItemDto>();
        foreach (var title in new[] { "A", "B", "C" })
        {
            tasks[title] = (await Create(NewTask(project, title))).Value;
        }

        foreach (var title in new[] { "D", "E" })
        {
            tasks[title] = (await Create(NewTask(project, title, columnId: ColumnAt(project, 1).Id))).Value;
        }

        (await Board(project)).Should().Be("A B C | D E | ");
        return (owner, project, tasks);
    }

    [Fact]
    public async Task Move_ToAPlaceInAnotherColumn_PutsItThereAndCloseTheGapItLeft()
    {
        var (_, project, tasks) = await ABoard();

        var result = await Move(tasks["A"].Id, ColumnAt(project, 1), 1);

        result.Value.Should().BeEquivalentTo(new { ColumnId = ColumnAt(project, 1).Id, ColumnName = "În lucru", Position = 1 });
        (await Board(project)).Should().Be("B C | D A E | ");
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

        (await Move(tasks[title].Id, ColumnAt(project, 0), position)).Value.Position.Should().Be(position);

        (await Board(project)).Should().Be($"{order} | D E | ");
    }

    [Fact]
    public async Task Move_WithoutAPlace_PutsItAtTheEnd()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["A"].Id, ColumnAt(project, 1))).Value.Position.Should().Be(2);

        (await Board(project)).Should().Be("B C | D E A | ");
    }

    [Fact]
    public async Task Move_PastTheEnd_PutsItAtTheEnd()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["E"].Id, ColumnAt(project, 0), 99)).Value.Position.Should().Be(3);

        (await Board(project)).Should().Be("A B C E | D | ");
    }

    [Fact]
    public async Task Move_IntoTheDoneColumn_CompletesItAndOutClearsThat()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["A"].Id, DoneColumn(project))).Value.CompletedAt.Should().Be(Now);
        (await Move(tasks["A"].Id, ColumnAt(project, 0), 0)).Value.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Move_ToAnotherColumn_WritesTheColumnsInTheHistoryAndAReorderDoesNot()
    {
        var (owner, project, tasks) = await ABoard();

        await Move(tasks["A"].Id, ColumnAt(project, 1), 0);
        await Move(tasks["A"].Id, ColumnAt(project, 1), 2);

        (await Stored(tasks["A"].Id))!.History.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Field = "ColumnId",
            OldValue = ColumnAt(project, 0).Id.ToString(),
            NewValue = ColumnAt(project, 1).Id.ToString(),
            ChangedById = owner.Id,
        });
        (await Stored(tasks["B"].Id))!.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_PastAnArchivedTask_GivesItNoPlace()
    {
        var (_, project, tasks) = await ABoard();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(tasks["B"].Id))).IsSuccess.Should().BeTrue();

        (await Move(tasks["D"].Id, ColumnAt(project, 0))).Value.Position.Should().Be(2);

        (await Board(project)).Should().Be("A C D | E | ");
    }

    [Fact]
    public async Task Move_ToAColumnOfAnotherProject_ReturnsAValidationErrorOnColumnIdAndMovesNothing()
    {
        var (owner, project, tasks) = await ABoard();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        var result = await Move(tasks["A"].Id, ColumnAt(other, 0), 0);

        result.Error.Should().Be(TaskErrors.ColumnNotInProject);
        (await Board(project)).Should().Be("A B C | D E | ");
    }

    [Fact]
    public async Task Move_ToANegativePlace_ReturnsAValidationErrorOnPosition()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["A"].Id, ColumnAt(project, 1), -1)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Position");
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.ProjectsManage)]
    public async Task Move_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired(string permission)
    {
        var (_, project, tasks) = await ABoard();
        await SignInWith(permission);

        (await Move(tasks["A"].Id, ColumnAt(project, 1), 0)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task Move_InAnArchivedProject_ReturnsArchived()
    {
        var (owner, project, tasks) = await ABoard();
        await Archive(project, owner);

        (await Move(tasks["A"].Id, ColumnAt(project, 1), 0)).Error.Should().Be(ProjectErrors.Archived);
    }

    [Fact]
    public async Task Move_OfAnArchivedTask_ReturnsNotFound()
    {
        var (_, project, tasks) = await ABoard();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(tasks["A"].Id))).IsSuccess.Should().BeTrue();

        (await Move(tasks["A"].Id, ColumnAt(project, 1), 0)).Error.Should().Be(TaskErrors.NotFound);
    }

    // SPEC section 5.3: two moves through the same column at once; the second changes nothing.
    [Fact]
    public async Task Move_TwoAtOnceThroughTheSameColumn_GiveTheSecondChangedMeanwhile()
    {
        var (owner, project, tasks) = await ABoard();

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(tasks["C"].Id, ColumnAt(project, 0).Id, 0), owner),
            async () => (await HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(tasks["A"].Id, ColumnAt(project, 1).Id, 0), owner)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        (await Board(project)).Should().Be("B C | A D E | ");
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
        var moved = (await Create(NewTask(project, "Nou", columnId: ColumnAt(project, 1).Id))).Value;

        await Move(moved.Id, ColumnAt(project, 0), 1);

        (await Board(project)).Should().Be("Unu Nou Doi Trei |  | ");
    }

    [Fact]
    public async Task Move_ToTheSamePlace_ChangesNothing()
    {
        var (_, project, tasks) = await ABoard();

        (await Move(tasks["B"].Id, ColumnAt(project, 0), 1)).Value.Position.Should().Be(1);

        (await Board(project)).Should().Be("A B C | D E | ");
        (await Stored(tasks["B"].Id))!.UpdatedAt.Should().BeNull();
    }
}
