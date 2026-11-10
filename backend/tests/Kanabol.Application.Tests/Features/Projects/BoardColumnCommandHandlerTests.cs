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
/// The columns of a board (UI spec section 7.4): an owner or projects.manage adds, renames, moves and
/// deletes them, whoever sees the project reads them. Every board has the four fixed columns, and Gata is
/// the done one (SPEC section 4.4). The places stay 0, 1, 2..., and the project's version keeps two
/// changes at once from both landing.
/// </summary>
public sealed class BoardColumnCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<BoardColumnDto>> Add(Project project, string name) =>
        Handle<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, name));

    private Task<Result<BoardColumnDto>> Update(Project project, BoardColumn column, string name) => Update(project, column.Id, name);

    private Task<Result<BoardColumnDto>> Update(Project project, Guid columnId, string name) =>
        Handle<UpdateBoardColumnCommand, BoardColumnDto>(new UpdateBoardColumnCommand(project.Id, columnId, name));

    private Task<Result<BoardColumnDto[]>> MoveColumn(Project project, BoardColumn column, int position) => MoveColumn(project, column.Id, position);

    private Task<Result<BoardColumnDto[]>> MoveColumn(Project project, Guid columnId, int position) =>
        Handle<MoveBoardColumnCommand, BoardColumnDto[]>(new MoveBoardColumnCommand(project.Id, columnId, position));

    private Task<Result<Unit>> Delete(Project project, BoardColumn column) => Delete(project, column.Id);

    private Task<Result<Unit>> Delete(Project project, Guid columnId) =>
        Handle<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, columnId));

    /// <summary>The columns as stored, from the left, as "Name" or "Name*" for Gata, the done one.</summary>
    private async Task<string> ColumnsOf(Project project)
    {
        await using var db = Postgres.CreateContext();
        var columns = await db.BoardColumns.Where(column => column.ProjectId == project.Id).OrderBy(column => column.Position).ToListAsync();
        columns.Select(column => column.Position).Should().Equal(Enumerable.Range(0, columns.Count), "the places stay 0, 1, 2...");
        return string.Join(" | ", columns.Select(column => column.Kind == ColumnKind.Done ? $"{column.Name}*" : column.Name));
    }

    private async Task<(User Owner, Project Project)> AProject()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("COL").OwnedBy(owner));
        SignIn(owner);
        return (owner, project);
    }

    [Fact]
    public async Task Add_ByAnOwner_MakesACustomColumn()
    {
        var (_, project) = await AProject();

        var result = await Add(project, "  Review ");

        result.Value.Should().BeEquivalentTo(new { Name = "Review", Position = 4, Kind = ColumnKind.Custom });
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata* | Review");
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
        (await Update(project, ColumnAt(project, 0), "Altceva")).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await MoveColumn(project, ColumnAt(project, 0), 2)).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await Delete(project, ColumnAt(project, 1))).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
    }

    [Fact]
    public async Task Changes_InAnArchivedProject_ReturnArchived()
    {
        var (owner, project) = await AProject();
        await Archive(project, owner);

        (await Add(project, "Review")).Error.Should().Be(ProjectErrors.Archived);
        (await Update(project, ColumnAt(project, 0), "Altceva")).Error.Should().Be(ProjectErrors.Archived);
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
        (await Update(project, ColumnAt(project, 0), new string('a', 101))).Error.Should().BeOfType<ValidationError>();
        (await Add(project, new string('a', 100))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_TheName_ChangesOnlyTheName()
    {
        var (_, project) = await AProject();
        var review = (await Add(project, "Review")).Value;

        var result = await Update(project, review.Id, " Verificare ");

        result.Value.Should().BeEquivalentTo(new { review.Id, Name = "Verificare", Position = 4, Kind = ColumnKind.Custom });
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata* | Verificare");
    }

    [Theory]
    [InlineData(1, "Backlog | Review | În lucru | În pauză | Gata*")]
    [InlineData(3, "Backlog | În lucru | În pauză | Review | Gata*")]
    public async Task Move_ToAPlace_PutsItThereAndAnswersEveryColumnInOrder(int position, string expected)
    {
        var (_, project) = await AProject();
        var review = (await Add(project, "Review")).Value;

        var result = await MoveColumn(project, review.Id, position);

        string.Join(" | ", result.Value.Select(column => column.Kind == ColumnKind.Done ? $"{column.Name}*" : column.Name)).Should().Be(expected);
        result.Value.Select(column => column.Position).Should().Equal(0, 1, 2, 3, 4);
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
        var review = (await Add(project, "Review")).Value;
        await MoveColumn(project, review.Id, 1);

        (await Delete(project, review.Id)).IsSuccess.Should().BeTrue();

        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
        Logs.Entries.Should().Contain(entry => entry.Message == $"Column {review.Id} deleted from project {project.Id}");
    }

    [Fact]
    public async Task Delete_AColumnWithATask_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        await CreateTaskAs(owner, project);

        (await Delete(project, ColumnAt(project, 0))).Error.Should().Be(ColumnErrors.NotEmpty);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
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
    public async Task Changes_ToAColumnOfAnotherProject_ReturnNotFound()
    {
        var (owner, project) = await AProject();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        (await Update(project, ColumnAt(other, 0), "X")).Error.Should().Be(ColumnErrors.NotFound);
        (await MoveColumn(project, ColumnAt(other, 0), 0)).Error.Should().Be(ColumnErrors.NotFound);
        (await Delete(project, ColumnAt(other, 1))).Error.Should().Be(ColumnErrors.NotFound);
        (await ColumnsOf(other)).Should().Be("Backlog | În lucru | În pauză | Gata*");
    }

    [Fact]
    public async Task Read_ForOneWhoSeesTheProject_GivesTheColumnsFromTheLeft()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        await SignInWith(Permissions.ProjectsViewAll);

        (await Ask<GetBoardColumnsQuery, BoardColumnDto[]>(new GetBoardColumnsQuery(project.Id))).Value
            .Select(column => column.Name).Should().Equal("Backlog", "În lucru", "În pauză", "Gata");
        (await Ask<GetBoardColumnByIdQuery, BoardColumnDto>(new GetBoardColumnByIdQuery(project.Id, ColumnAt(project, 3).Id))).Value
            .Should().Be(new BoardColumnDto(ColumnAt(project, 3).Id, "Gata", 3, ColumnKind.Done));
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
            () => HandleInOwnScope<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, "Prima")),
            async () => (await HandleInOwnScope<AddBoardColumnCommand, BoardColumnDto>(new AddBoardColumnCommand(project.Id, "A doua"))).IsSuccess.Should().BeTrue());

        first.Error.Should().Be(ProjectErrors.ChangedMeanwhile);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata* | A doua");
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
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
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
