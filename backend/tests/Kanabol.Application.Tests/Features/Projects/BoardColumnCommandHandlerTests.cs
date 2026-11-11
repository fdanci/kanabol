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
/// deletes the project's own, whoever sees the project reads them. Backlog, În lucru, În pauză and Gata
/// are in every board and never change, the project's own stay between Backlog and Gata, and Gata is the
/// done one (SPEC section 4.4). The places stay 0, 1, 2..., and the project's version keeps two changes at
/// once from both landing.
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

    // A request of its own, as the API makes one: a task created here took its number through the project's row,
    // whose version the test's own scope read before.
    private Task<Result<BoardColumnDto[]>> MoveColumnApart(Project project, Guid columnId, int position) =>
        HandleInOwnScope<MoveBoardColumnCommand, BoardColumnDto[]>(new MoveBoardColumnCommand(project.Id, columnId, position));

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

    private static BoardColumn FixedColumn(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    public static TheoryData<ColumnKind> FixedKinds => [ColumnKind.Backlog, ColumnKind.InProgress, ColumnKind.Paused, ColumnKind.Done];

    /// <summary>A column of the project's own, Review, right before Gata, holding a task when asked.</summary>
    private async Task<BoardColumnDto> AColumnOfItsOwn(Project project, User owner, bool withATask = false)
    {
        var review = (await Add(project, "Review")).Value;
        if (withATask)
        {
            var task = await CreateTaskAs(owner, project, "În review");
            (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, review.Id, null))).IsSuccess.Should().BeTrue();
        }

        return review;
    }

    [Fact]
    public async Task Add_ByAnOwner_PutsACustomColumnRightBeforeGata()
    {
        var (_, project) = await AProject();

        var result = await Add(project, "  Review ");
        await Add(project, "QA");

        result.Value.Should().BeEquivalentTo(new { Name = "Review", Position = 3, Kind = ColumnKind.Custom });
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | QA | Gata*");
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

    // SPEC section 4.4, group 23: a real member whose role has columns.manage adds, renames, moves and deletes.
    [Fact]
    public async Task Changes_ByAMemberWithColumnsManage_AddRenameMoveAndDelete()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com", permissions: Permissions.ColumnsManage);
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);

        var review = (await Add(project, "Review")).Value;
        await Add(project, "QA");
        (await Update(project, review.Id, "În review")).IsSuccess.Should().BeTrue();
        (await MoveColumn(project, review.Id, 4)).IsSuccess.Should().BeTrue();
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | QA | În review | Gata*");
        (await Delete(project, review.Id)).IsSuccess.Should().BeTrue();
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | QA | Gata*");
    }

    [Fact]
    public async Task Changes_WithColumnsManageOutsideTheProject_ReturnOwnerRequiredAndChangeNothing()
    {
        var owner = await CreateUser();
        var outsider = await CreateUser("dan@example.com", permissions: [Permissions.ColumnsManage, Permissions.ProjectsViewAll]);
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(outsider);

        (await Add(project, "Review")).Error.Should().Be(ProjectErrors.OwnerRequired);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
    }

    // Read from the database: a token still holding columns.manage after the role lost it changes nothing.
    [Fact]
    public async Task Changes_WhenOnlyTheTokenHasColumnsManage_ReturnOwnerRequired()
    {
        var owner = await CreateUser();
        var member = await CreateUser("dan@example.com");
        var project = await CreateProject(builder => builder.OwnedBy(owner).WithMember(member));
        SignIn(member);
        CurrentUser.HasPermission(Permissions.ColumnsManage).Returns(true);

        (await Add(project, "Review")).Error.Should().Be(ProjectErrors.OwnerRequired);
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

    // SPEC section 4.4: the fixed names are taken, in any case, for a new column and for a new name, and
    // from group 23 Arhivate's too.
    [Theory]
    [InlineData("Backlog")]
    [InlineData("backlog")]
    [InlineData("ÎN LUCRU")]
    [InlineData(" În pauză ")]
    [InlineData("gata")]
    [InlineData("Arhivate")]
    [InlineData(" ARHIVATE ")]
    public async Task AddAndUpdate_WithAReservedName_ReturnAValidationErrorOnName(string name)
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner);

        (await Add(project, name)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
        (await Update(project, review.Id, name)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Name");
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
    }

    [Fact]
    public async Task Update_TheName_ChangesOnlyTheName()
    {
        var (_, project) = await AProject();
        var review = (await Add(project, "Review")).Value;

        var result = await Update(project, review.Id, " Verificare ");

        result.Value.Should().BeEquivalentTo(new { review.Id, Name = "Verificare", Position = 3, Kind = ColumnKind.Custom });
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Verificare | Gata*");
    }

    // SPEC section 4.4: a fixed column is never renamed, moved or deleted, empty or not.
    [Theory]
    [MemberData(nameof(FixedKinds))]
    public async Task Changes_ToAFixedColumn_ReturnFixedAndChangeNothing(ColumnKind kind)
    {
        var (_, project) = await AProject();
        var column = FixedColumn(project, kind);

        (await Update(project, column, "Altceva")).Error.Should().Be(ColumnErrors.Fixed);
        (await MoveColumn(project, column, 1)).Error.Should().Be(ColumnErrors.Fixed);
        (await Delete(project, column)).Error.Should().Be(ColumnErrors.Fixed);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Gata*");
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

    // SPEC section 4.4: Backlog stays first and Gata last, so a column of the project's own goes only between them.
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(9)]
    public async Task Move_BeforeBacklogOrAfterGata_ReturnsAValidationErrorOnPositionAndMovesNothing(int position)
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner);

        (await MoveColumn(project, review.Id, position)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("Position");
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
    }

    // SPEC section 4.4, group 23: a column holding tasks, archived ones too, doesn't cross În lucru, either way.
    [Fact]
    public async Task Move_AColumnWithATaskToBeforeInProgress_ReturnsCrossesInProgressAndMovesNothing()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner, withATask: true);

        var result = await MoveColumnApart(project, review.Id, 1);

        result.Error.Should().Be(ColumnErrors.CrossesInProgress);
        result.Error.Type.Should().Be(ErrorType.Conflict);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
    }

    [Fact]
    public async Task Move_AColumnWithATaskFromBeforeInProgressToAfterIt_ReturnsCrossesInProgress()
    {
        var (owner, project) = await AProject();
        var analysis = (await Add(project, "Analiză")).Value;
        (await MoveColumn(project, analysis.Id, 1)).IsSuccess.Should().BeTrue();
        var task = await CreateTaskAs(owner, project, "De analizat");
        (await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, analysis.Id, null))).IsSuccess.Should().BeTrue();

        (await MoveColumnApart(project, analysis.Id, 3)).Error.Should().Be(ColumnErrors.CrossesInProgress);
        (await ColumnsOf(project)).Should().Be("Backlog | Analiză | În lucru | În pauză | Gata*");
    }

    [Fact]
    public async Task Move_AColumnWithOnlyAnArchivedTaskAcrossInProgress_ReturnsCrossesInProgress()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner, withATask: true);
        var task = (await TasksOf(project)).Single();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        (await MoveColumnApart(project, review.Id, 1)).Error.Should().Be(ColumnErrors.CrossesInProgress);
    }

    [Fact]
    public async Task Move_AColumnWithATaskOnItsSideOfInProgress_Moves()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner, withATask: true);

        (await MoveColumnApart(project, review.Id, 2)).IsSuccess.Should().BeTrue();
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | Review | În pauză | Gata*");
    }

    [Fact]
    public async Task Move_AnEmptyColumn_CrossesInProgressBothWays()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner);

        (await MoveColumnApart(project, review.Id, 1)).IsSuccess.Should().BeTrue();
        (await ColumnsOf(project)).Should().Be("Backlog | Review | În lucru | În pauză | Gata*");
        (await MoveColumnApart(project, review.Id, 3)).IsSuccess.Should().BeTrue();
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
    }

    // A refusal of the place comes first: 400 on position, as the other checks of the form.
    [Fact]
    public async Task Move_AColumnWithATaskBeforeBacklog_ReturnsAValidationErrorOnPositionFirst()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner, withATask: true);

        (await MoveColumnApart(project, review.Id, 0)).Error.Should().BeOfType<ValidationError>();
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
        var review = await AColumnOfItsOwn(project, owner, withATask: true);

        (await Delete(project, review.Id)).Error.Should().Be(ColumnErrors.NotEmpty);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
    }

    // An archived task keeps its column, and goes back to it when restored, so it counts.
    [Fact]
    public async Task Delete_AColumnWithOnlyAnArchivedTask_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner, withATask: true);
        var task = (await TasksOf(project)).Single();
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        (await Delete(project, review.Id)).Error.Should().Be(ColumnErrors.NotEmpty);
    }

    [Fact]
    public async Task Changes_ToAColumnOfAnotherProject_ReturnNotFound()
    {
        var (owner, project) = await AProject();
        var other = await CreateProject(builder => builder.OwnedBy(owner));

        (await Update(project, ColumnAt(other, 0), "X")).Error.Should().Be(ColumnErrors.NotFound);
        (await MoveColumn(project, ColumnAt(other, 0), 1)).Error.Should().Be(ColumnErrors.NotFound);
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

    // The project is saved with its columns, so of two additions at once only one takes the place before Gata.
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
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | A doua | Gata*");
    }

    // A task moved into an empty column after the delete checked it: the foreign key refuses the delete.
    [Fact]
    public async Task Delete_WhileATaskIsMovedIntoIt_ReturnsNotEmpty()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner);
        var task = await CreateTaskAs(owner, project);

        var deleted = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, review.Id)),
            async () => (await HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, review.Id, null))).IsSuccess.Should().BeTrue());

        deleted.Error.Should().Be(ColumnErrors.NotEmpty);
        (await ColumnsOf(project)).Should().Be("Backlog | În lucru | În pauză | Review | Gata*");
        (await Stored(task.Id))!.ColumnId.Should().Be(review.Id);
    }

    // The other way round: the column goes first, and the move answers as for a column not on the board.
    [Fact]
    public async Task MoveTask_IntoAColumnDeletedMeanwhile_ReturnsAValidationErrorOnColumnId()
    {
        var (owner, project) = await AProject();
        var review = await AColumnOfItsOwn(project, owner);
        var task = await CreateTaskAs(owner, project);

        // The move's first read of the clock comes after it read the column.
        var moved = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, review.Id, null)),
            async () => (await HandleInOwnScope<DeleteBoardColumnCommand, Unit>(new DeleteBoardColumnCommand(project.Id, review.Id))).IsSuccess.Should().BeTrue());

        moved.Error.Should().Be(TaskErrors.ColumnNotInProject);
        (await Stored(task.Id))!.ColumnId.Should().Be(ColumnAt(project, 0).Id);
    }
}
