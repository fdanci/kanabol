using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Projects.Commands.AddBoardColumn;
using Kanabol.Application.Features.Projects.Commands.DeleteBoardColumn;
using Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;
using Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;
using Kanabol.Application.Features.Projects.Queries.GetBoardColumnById;
using Kanabol.Application.Features.Projects.Queries.GetBoardColumns;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Tests.Features.Tasks;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Projects;

/// <summary>
/// The columns of a board (UI spec section 7.4): an owner or projects.manage adds, renames, marks done,
/// moves and deletes them, whoever sees the project reads them. A column that becomes done completes
/// the tasks in it (SPEC section 5.1). The places stay 0, 1, 2..., and the project's version keeps two
/// changes at once from both landing.
/// </summary>
public sealed class BoardColumnCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<BoardColumnDto>> Add(Project project, string name, bool isDone = false) =>
        Handle<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, name, isDone));

    private Task<Result<BoardColumnDto>> Update(Project project, BoardColumn column, string name, bool isDone) =>
        Handle<UpdateBoardColumnCommand, BoardColumnDto>(new UpdateBoardColumnCommand(project.Id, column.Id, name, isDone));

    private Task<Result<BoardColumnDto[]>> MoveColumn(Project project, BoardColumn column, int position) =>
        Handle<MoveBoardColumnCommand, BoardColumnDto[]>(new MoveBoardColumnCommand(project.Id, column.Id, position));

    private Task<Result<Unit>> Delete(Project project, BoardColumn column) =>
        Handle<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, column.Id));

    /// <summary>The columns as stored, from the left, as "Name" or "Name*" for a done one.</summary>
    private async Task<string> ColumnsOf(Project project)
    {
        await using var db = Postgres.CreateContext();
        var columns = await db.BoardColumns.Where(column => column.ProjectId == project.Id).OrderBy(column => column.Position).ToListAsync();
        columns.Select(column => column.Position).Should().Equal(Enumerable.Range(0, columns.Count), "the places stay 0, 1, 2...");
        return string.Join(" | ", columns.Select(column => column.IsDone ? $"{column.Name}*" : column.Name));
    }

    private async Task<(User Owner, Project Project)> AProject()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("COL").OwnedBy(owner));
        SignIn(owner);
        return (owner, project);
    }

    [Fact]
    public async Task Add_ByAnOwner_PutsItLastWithItsFlag()
    {
        var (_, project) = await AProject();

        var result = await Add(project, "  Review ", isDone: true);

        result.Value.Should().BeEquivalentTo(new { Name = "Review", Position = 3, IsDone = true });
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru | Gata* | Review*");
        Logs.Entries.Should().Contain(entry => entry.Message == $"Column {result.Value.Id} added to project {project.Id}");
    }

    [Fact]
    public async Task Add_WithProjectsManage_ChangesAProjectTheyArentIn()
    {
        var (_, project) = await AProject();
        await SignInWith(Permissions.ProjectsManage);

        (await Add(project, "Review")).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Permissions.ProjectsViewAll)]
    [InlineData(Permissions.TasksCreate)]
    public async Task Changes_ByOneWhoIsNotAnOwner_ReturnOwnerRequiredAndChangeNothing(string permission)
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com", permissions: permission);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        (await Add(project, "Review")).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Update(project, ColumnAt(project, 0), "Altceva", isDone: false)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await MoveColumn(project, ColumnAt(project, 0), 2)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Delete(project, ColumnAt(project, 1))).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru | Gata*");
    }

    [Fact]
    public async Task Changes_InAnArchivedProject_ReturnArchived()
    {
        var (owner, project) = await AProject();
        await Archive(project, owner);

        (await Add(project, "Review")).Error.Should().Be(ProjectErrors.Archived);
        (await Update(project, ColumnAt(project, 0), "Altceva", isDone: false)).Error.Should().Be(ProjectErrors.Archived);
        (await MoveColumn(project, ColumnAt(project, 0), 2)).Error.Should().Be(ProjectErrors.Archived);
        (await Delete(project, ColumnAt(project, 1))).Error.Should().Be(ProjectErrors.Archived);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Re\u0000view")]
    public async Task Add_WithANameThatIsBlankOrHasHiddenCharacters_ReturnsAValidationErrorOnName(string name)
    {
        var (_, project) = await AProject();

        (await Add(project, name)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
    }

    // UI spec section 7.4 gives no limit; the column holds 100 characters, as since group 11.
    [Fact]
    public async Task AddAndUpdate_WithANameOverTheLimit_ReturnAValidationErrorOnName()
    {
        var (_, project) = await AProject();

        (await Add(project, new string('a', 101))).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
        (await Update(project, ColumnAt(project, 0), new string('a', 101), isDone: false)).Error.Should().BeOfType<ValidationError>();
        (await Add(project, new string('a', 100))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_TheName_ChangesOnlyTheName()
    {
        var (_, project) = await AProject();

        var result = await Update(project, ColumnAt(project, 1), " Lucrez ", isDone: false);

        result.Value.Should().BeEquivalentTo(new { Name = "Lucrez", Position = 1, IsDone = false });
        (await ColumnsOf(project)).Should().Be("De făcut | Lucrez | Gata*");
    }

    // SPEC section 5.1: in a done column a task is completed, and outside one it's open.
    [Fact]
    public async Task Update_ToADoneColumnAndBack_CompletesItsOpenTasksThenOpensThem()
    {
        var (owner, project) = await AProject();
        var open = await CreateTaskAs(owner, project, "Deschis");
        var archived = await CreateTaskAs(owner, project, "Arhivat");
        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(open.Id, ColumnAt(project, 1).Id, null))).IsSuccess.Should().BeTrue();
        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(archived.Id, ColumnAt(project, 1).Id, null))).IsSuccess.Should().BeTrue();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(archived.Id))).IsSuccess.Should().BeTrue();

        (await Update(project, ColumnAt(project, 1), "În lucru", isDone: true)).Value.IsDone.Should().BeTrue();

        (await Stored(open.Id))!.CompletedAt.Should().Be(Now);
        (await Stored(archived.Id))!.CompletedAt.Should().BeNull("an archived task keeps what it had when it was archived");
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru* | Gata*");
        (await Stored(open.Id))!.History.Should().ContainSingle("the column, not the task, changed: only the move is in the history");

        (await Update(project, ColumnAt(project, 1), "În lucru", isDone: false)).IsSuccess.Should().BeTrue();

        (await Stored(open.Id))!.CompletedAt.Should().BeNull();
    }

    [Theory]
    [InlineData(0, "Gata* | De făcut | În lucru")]
    [InlineData(1, "De făcut | Gata* | În lucru")]
    [InlineData(7, "De făcut | În lucru | Gata*")]
    public async Task Move_ToAPlace_PutsItThereAndAnswersEveryColumnInOrder(int position, string expected)
    {
        var (_, project) = await AProject();

        var result = await MoveColumn(project, ColumnAt(project, 2), position);

        string.Join(" | ", result.Value.Select(column => column.IsDone ? $"{column.Name}*" : column.Name)).Should().Be(expected);
        result.Value.Select(column => column.Position).Should().Equal(0, 1, 2);
        (await ColumnsOf(project)).Should().Be(expected);
    }

    [Fact]
    public async Task Move_ToANegativePlace_ReturnsAValidationErrorOnPosition()
    {
        var (_, project) = await AProject();

        (await MoveColumn(project, ColumnAt(project, 2), -1)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Position");
    }

    [Fact]
    public async Task Delete_AnEmptyColumn_TakesItOutAndClosesTheGap()
    {
        var (_, project) = await AProject();

        (await Delete(project, ColumnAt(project, 1))).IsSuccess.Should().BeTrue();

        (await ColumnsOf(project)).Should().Be("De făcut | Gata*");
        Logs.Entries.Should().Contain(entry => entry.Message == $"Column {ColumnAt(project, 1).Id} deleted from project {project.Id}");
    }

    [Fact]
    public async Task Delete_AColumnWithATask_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        await CreateTaskAs(owner, project);

        (await Delete(project, ColumnAt(project, 0))).Error.Should().Be(ColumnErrors.NotEmpty);
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru | Gata*");
    }

    // An archived task keeps its column, and goes back to it when restored, so it counts.
    [Fact]
    public async Task Delete_AColumnWithOnlyAnArchivedTask_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        var task = await CreateTaskAs(owner, project);
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        (await Delete(project, ColumnAt(project, 0))).Error.Should().Be(ColumnErrors.NotEmpty);
    }

    [Fact]
    public async Task Delete_TheLastColumn_ReturnsLastColumn()
    {
        var (_, project) = await AProject();
        (await Delete(project, ColumnAt(project, 2))).IsSuccess.Should().BeTrue();
        (await Delete(project, ColumnAt(project, 1))).IsSuccess.Should().BeTrue();

        (await Delete(project, ColumnAt(project, 0))).Error.Should().Be(ColumnErrors.LastColumn);
        (await ColumnsOf(project)).Should().Be("De făcut");
    }

    [Fact]
    public async Task Changes_ToAColumnOfAnotherProject_ReturnNotFound()
    {
        var (owner, project) = await AProject();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        (await Update(project, ColumnAt(other, 0), "X", isDone: false)).Error.Should().Be(ColumnErrors.NotFound);
        (await MoveColumn(project, ColumnAt(other, 0), 0)).Error.Should().Be(ColumnErrors.NotFound);
        (await Delete(project, ColumnAt(other, 1))).Error.Should().Be(ColumnErrors.NotFound);
        (await ColumnsOf(other)).Should().Be("De făcut | În lucru | Gata*");
    }

    [Fact]
    public async Task Read_ForOneWhoSeesTheProject_GivesTheColumnsFromTheLeft()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(Permissions.ProjectsViewAll);

        (await Ask<GetBoardColumnsQuery, BoardColumnDto[]>(new GetBoardColumnsQuery(project.Id))).Value
            .Select(column => column.Name).Should().Equal("De făcut", "În lucru", "Gata");
        (await Ask<GetBoardColumnByIdQuery, BoardColumnDto>(new GetBoardColumnByIdQuery(project.Id, ColumnAt(project, 2).Id))).Value
            .Should().Be(new BoardColumnDto(ColumnAt(project, 2).Id, "Gata", 2, true));
        (await Ask<GetBoardColumnByIdQuery, BoardColumnDto>(new GetBoardColumnByIdQuery(project.Id, Guid.CreateVersion7()))).Error
            .Should().Be(ColumnErrors.NotFound);
    }

    [Fact]
    public async Task Read_ForSomeoneOutsideTheProject_ReturnsMemberRequired()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(await CreateUser("dan@example.com"));

        (await Ask<GetBoardColumnsQuery, BoardColumnDto[]>(new GetBoardColumnsQuery(project.Id))).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    // The project is saved with its columns, so of two additions at once only one takes the last place.
    [Fact]
    public async Task Add_TwoAtOnce_TheSecondGets409AndThePlacesStayInOrder()
    {
        var (_, project) = await AProject();

        // The first read of the clock is the audit stamp of the save.
        var first = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, "Prima", false)),
            async () => (await HandleInOwnScope<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, "A doua", false))).IsSuccess.Should().BeTrue());

        first.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru | Gata* | A doua");
    }

    // A task moved into an empty column after the delete checked it: the foreign key refuses the delete.
    [Fact]
    public async Task Delete_WhileATaskIsMovedIntoIt_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        var deleted = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, ColumnAt(project, 1).Id)),
            async () => (await HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, ColumnAt(project, 1).Id, null))).IsSuccess.Should().BeTrue());

        deleted.Error.Should().Be(ColumnErrors.NotEmpty);
        (await ColumnsOf(project)).Should().Be("De făcut | În lucru | Gata*");
        (await Stored(task.Id))!.ColumnId.Should().Be(ColumnAt(project, 1).Id);
    }

    // The other way round: the column goes first, and the move answers as for a column not on the board.
    [Fact]
    public async Task MoveTask_IntoAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnId()
    {
        var (owner, project) = await AProject();
        var task = await CreateTaskAs(owner, project);

        // The move's first read of the clock comes after it read the column.
        var moved = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, ColumnAt(project, 1).Id, null)),
            async () => (await HandleInOwnScope<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, ColumnAt(project, 1).Id))).IsSuccess.Should().BeTrue());

        moved.Error.Should().Be(TaskErrors.ColumnNotInProject);
        (await Stored(task.Id))!.ColumnId.Should().Be(ColumnAt(project, 0).Id);
    }

    [Fact]
    public async Task CreateTask_InAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnIdAndTakesNoNumber()
    {
        var (_, project) = await AProject();

        // The creation's first read of the clock comes after it read the column, before it takes a number.
        var created = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<Application.Features.Tasks.Commands.CreateTask.CreateTaskCommand, TaskItemDto>(NewTask(project, columnId: ColumnAt(project, 1).Id)),
            async () => (await HandleInOwnScope<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, ColumnAt(project, 1).Id))).IsSuccess.Should().BeTrue());

        created.Error.Should().Be(TaskErrors.ColumnNotInProject);
        (await TasksOf(project)).Should().BeEmpty();
        (await NextTaskNumberOf(project)).Should().Be(1, "the number went back with the rest of the transaction");
    }
}
