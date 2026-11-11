using Kanabol.Application.Common;
using Kanabol.Application.Features.Auth;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Queries.GetArchivedTasks;
using Kanabol.Application.Features.Tasks.Queries.GetBoard;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using NSubstitute;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The column "Arhivate" (SPEC section 4.4, group 22): it isn't a row of board_columns. GET …/board/archived
/// gives tasks.archive, from the token, a page of the project's archived cards, from the latest archiving,
/// each with who archived it and the column it comes back to; the board says canSeeArchived.
/// </summary>
public sealed class GetArchivedTasksQueryHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private Task<Result<Page<ArchivedTaskDto>>> Archived(Guid projectId, int page = 1, int pageSize = 50) =>
        Ask<GetArchivedTasksQuery, Page<ArchivedTaskDto>>(new GetArchivedTasksQuery(projectId, page, pageSize));

    private static BoardColumn Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    private async Task Archive(TaskItemDto task, DateTimeOffset at)
    {
        Clock.UtcNow.Returns(at);
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();
    }

    private async Task<(User Owner, Project Project)> AProject()
    {
        var owner = await CreateUserNamed("Ana Pop", "ana@example.com");
        var project = await CreateProject(builder => builder.WithKey("ARH").OwnedBy(owner));
        SignIn(owner);
        HoldsInToken(Permissions.TasksArchive);
        return (owner, project);
    }

    [Fact]
    public async Task Archived_WithTasksArchive_GivesTheArchivedCardsFromTheLatestWithWhoAndTheirColumn()
    {
        var (owner, project) = await AProject();
        var backlog = (await Create(NewTask(project, "Din Backlog"))).Value;
        var working = (await Create(NewTask(project, "Din lucru"))).Value;
        var paused = (await Create(NewTask(project, "Din pauză"))).Value;
        var open = (await Create(NewTask(project, "Rămâne"))).Value;
        await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(working.Id, Kind(project, ColumnKind.InProgress).Id, null, 120));
        await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(paused.Id, Kind(project, ColumnKind.Paused).Id, null, null, "Aștept clientul"));
        await Archive(backlog, Now.AddHours(1));
        await Archive(working, Now.AddHours(2));
        await Archive(paused, Now.AddHours(3));

        var page = (await Archived(project.Id)).Value;

        page.Should().BeEquivalentTo(new { Number = 1, PageSize = 50, TotalCount = 3 });
        page.Items.Select(card => card.Title).Should().Equal("Din pauză", "Din lucru", "Din Backlog");
        page.Items.Should().NotContain(card => card.Id == open.Id);
        page.Items[0].Should().BeEquivalentTo(new
        {
            paused.Id,
            Key = "ARH-3",
            ColumnId = Kind(project, ColumnKind.Paused).Id,
            ColumnName = "În pauză",
            ArchivedAt = Now.AddHours(3),
            ArchivedBy = new TaskPersonDto(owner.Id, "Ana Pop"),
            PauseReason = "Aștept clientul",
            IsWorking = false,
        });
        page.Items[1].Should().BeEquivalentTo(new
        {
            ColumnId = Kind(project, ColumnKind.InProgress).Id,
            ColumnName = "În lucru",
            EstimatedMinutes = 120,
            WorkedSeconds = 2 * 3600L,
            IsWorking = false,
        }, "the archive stopped the counter at 16:00 in Bucharest, two hours of the schedule after it started");
        page.Items[2].ColumnName.Should().Be("Backlog");
    }

    // Archived at one moment, they go by their id, so no card is on two pages.
    [Fact]
    public async Task Archived_InPages_GivesEachCardOnce()
    {
        var (_, project) = await AProject();
        var tasks = new List<TaskItemDto>();
        for (var number = 1; number <= 5; number++)
        {
            tasks.Add((await Create(NewTask(project, $"Task {number}"))).Value);
        }

        foreach (var task in tasks)
        {
            await Archive(task, Now);
        }

        var first = (await Archived(project.Id, page: 1, pageSize: 2)).Value;
        var second = (await Archived(project.Id, page: 2, pageSize: 2)).Value;
        var third = (await Archived(project.Id, page: 3, pageSize: 2)).Value;

        first.TotalCount.Should().Be(5);
        first.Items.Concat(second.Items).Concat(third.Items).Select(card => card.Id).Should().BeEquivalentTo(tasks.Select(task => task.Id))
            .And.OnlyHaveUniqueItems().And.BeInDescendingOrder();
    }

    [Fact]
    public async Task Archived_WithoutTasksArchive_Returns403PermissionRequired()
    {
        var (_, project) = await AProject();
        HoldsInToken();

        (await Archived(project.Id)).Error.Should().Be(AuthErrors.PermissionRequired);
    }

    // SPEC section 4.4: tasks.archive and seeing the project, or 403 and 404.
    [Fact]
    public async Task Archived_OfAProjectTheCallerDoesNotSeeOrThatDoesNotExist_Returns404()
    {
        var (_, project) = await AProject();
        var outsider = await CreateUser("eva@example.com");
        SignIn(outsider);
        HoldsInToken(Permissions.TasksArchive);

        (await Archived(project.Id)).Error.Should().Be(ProjectErrors.NotFound);
        (await Archived(Guid.CreateVersion7())).Error.Should().Be(ProjectErrors.NotFound);
    }

    [Fact]
    public async Task Archived_OfAnotherProjectOrOfAnArchivedOne_KeepsToItsOwn()
    {
        var (owner, project) = await AProject();
        var other = await CreateProject(builder => builder.WithKey("ALT").OwnedBy(owner));
        await Archive((await Create(NewTask(other, "Altul"))).Value, Now);
        await Archive((await Create(NewTask(project, "Al lui"))).Value, Now);
        await Archive(project, owner);

        (await Archived(project.Id)).Value.Items.Select(card => card.Title).Should().Equal("Al lui");
    }

    // SPEC section 4.4, group 23: the board counts the project's archived tasks, for tasks.archive only, so the
    // band shows its number before the column reads its cards.
    [Fact]
    public async Task Board_WithTasksArchive_CountsTheProjectsArchivedTasks()
    {
        var (owner, project) = await AProject();
        var first = (await Create(NewTask(project, "Primul"))).Value;
        var second = (await Create(NewTask(project, "Al doilea"))).Value;
        await Create(NewTask(project, "Rămâne"));
        var other = await CreateProject(builder => builder.WithKey("ALT").OwnedBy(owner));
        await Archive((await Create(NewTask(other, "Al altui proiect"))).Value, Now.AddHours(1));
        (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.ArchivedCount.Should().Be(0);

        await Archive(first, Now.AddHours(2));
        await Archive(second, Now.AddHours(3));

        (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.ArchivedCount.Should().Be(2);
    }

    [Fact]
    public async Task Board_WithoutTasksArchive_GivesNoArchivedCount()
    {
        var (_, project) = await AProject();
        var task = (await Create(NewTask(project, "Primul"))).Value;
        await Archive(task, Now.AddHours(1));
        HoldsInToken();

        (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.ArchivedCount.Should().BeNull();
    }

    [Fact]
    public async Task Board_SaysCanSeeArchived_AsTheTokenHoldsTasksArchive()
    {
        var (_, project) = await AProject();

        (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.CanSeeArchived.Should().BeTrue();
        HoldsInToken();
        (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value.CanSeeArchived.Should().BeFalse();
    }
}
