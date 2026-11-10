using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.SetTaskEstimate;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The estimate (SPEC section 4.4, "Mutarea între coloane, estimarea și pauza", group 22): asked for when a
/// task without one enters În lucru, replaced when one is sent then, ignored on every other move, optional
/// at creation, and changed any time through SetTaskEstimateCommand, to nothing only before the task first
/// entered În lucru. Each change is an EstimatedMinutes entry of the history.
/// </summary>
public sealed class TaskEstimateCommandHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private static BoardColumn Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    private Task<Result<TaskItemDto>> Move(Guid taskId, BoardColumn column, int? estimatedMinutes = null, int? position = null) =>
        Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(taskId, column.Id, position, estimatedMinutes));

    private Task<Result<TaskItemDto>> Estimate(Guid taskId, int? minutes) =>
        Handle<SetTaskEstimateCommand, TaskItemDto>(new SetTaskEstimateCommand(taskId, minutes));

    private async Task<(User Owner, Project Project, TaskItemDto Task)> ATaskInBacklog(int? estimatedMinutes = null)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.WithKey("EST").OwnedBy(owner));
        SignIn(owner);
        var task = (await Create(NewTask(project, "Login SSO") with { EstimatedMinutes = estimatedMinutes })).Value;
        return (owner, project, task);
    }

    private static IEnumerable<(string Field, string? Old, string? New)> Entries(TaskItem task) =>
        task.History.OrderBy(entry => entry.ChangedAt).ThenBy(entry => entry.Id).Select(entry => (entry.Field, entry.OldValue, entry.NewValue));

    [Fact]
    public async Task Move_IntoInProgressWithoutAnyEstimate_ReturnsAValidationErrorOnEstimatedMinutesAndMovesNothing()
    {
        var (_, project, task) = await ATaskInBacklog();

        var result = await Move(task.Id, Kind(project, ColumnKind.InProgress));

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("EstimatedMinutes");
        var stored = (await Stored(task.Id))!;
        stored.ColumnId.Should().Be(Kind(project, ColumnKind.Backlog).Id);
        stored.History.Should().BeEmpty();
    }

    [Fact]
    public async Task Move_IntoInProgressWithAnEstimate_SetsItAndWritesBothEntries()
    {
        var (owner, project, task) = await ATaskInBacklog();

        var result = await Move(task.Id, Kind(project, ColumnKind.InProgress), estimatedMinutes: 240);

        result.Value.Should().BeEquivalentTo(new { ColumnName = "În lucru", EstimatedMinutes = 240 });
        var stored = (await Stored(task.Id))!;
        Entries(stored).Should().BeEquivalentTo([
            (TaskHistoryFields.EstimatedMinutes, (string?)null, (string?)"240"),
            (TaskHistoryFields.ColumnId, Kind(project, ColumnKind.Backlog).Id.ToString(), Kind(project, ColumnKind.InProgress).Id.ToString()),
        ]);
        stored.History.Should().OnlyContain(entry => entry.ChangedById == owner.Id && entry.ChangedAt == Now);
    }

    [Theory]
    [InlineData(14)]
    [InlineData(60_001)]
    [InlineData(0)]
    public async Task Move_IntoInProgressWithAnEstimateOutOfItsLimits_ReturnsAValidationErrorOnEstimatedMinutes(int minutes)
    {
        var (_, project, task) = await ATaskInBacklog(estimatedMinutes: 60);

        var result = await Move(task.Id, Kind(project, ColumnKind.InProgress), estimatedMinutes: minutes);

        result.Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("EstimatedMinutes");
        (await Stored(task.Id))!.EstimatedMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Move_IntoInProgressOfATaskWithAnEstimate_NeedsNoneAndReplacesItWhenOneIsSent()
    {
        var (_, project, task) = await ATaskInBacklog(estimatedMinutes: 120);

        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).Value.EstimatedMinutes.Should().Be(120);
        (await Move(task.Id, Kind(project, ColumnKind.Paused))).IsSuccess.Should().BeTrue();
        (await Move(task.Id, Kind(project, ColumnKind.InProgress), estimatedMinutes: 300)).Value.EstimatedMinutes.Should().Be(300);
    }

    // SPEC section 4.4: outside the entry into În lucru the field is ignored, whatever it holds.
    [Theory]
    [InlineData(ColumnKind.Paused)]
    [InlineData(ColumnKind.Done)]
    public async Task Move_ElsewhereWithAnEstimate_IgnoresIt(ColumnKind kind)
    {
        var (_, project, task) = await ATaskInBacklog();

        var result = await Move(task.Id, Kind(project, kind), estimatedMinutes: 5);

        result.IsSuccess.Should().BeTrue();
        (await Stored(task.Id))!.EstimatedMinutes.Should().BeNull();
    }

    // A task in În lucru without an estimate, as the migration leaves them: a reorder there asks nothing.
    [Fact]
    public async Task Move_WithinInProgress_AsksForNothing()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var inProgress = Kind(project, ColumnKind.InProgress);
        var first = new TaskItemBuilder().InProject(project).InColumn(inProgress).WithTitle("Unu").AtPosition(0).Build();
        var second = new TaskItemBuilder().InProject(project).InColumn(inProgress).WithTitle("Doi").AtPosition(1).Build();
        await AddTasks(project, first, second);
        SignIn(owner);

        (await Move(second.Id, inProgress, position: 0)).Value.Position.Should().Be(0);
    }

    // SPEC section 4.4, the order: the access, Task.CannotMoveBack, then the fields the column asks for.
    [Fact]
    public async Task Move_WithoutAccessOrBack_SaysThatBeforeTheEstimate()
    {
        var (owner, project, task) = await ATaskInBacklog();
        (await Move(task.Id, Kind(project, ColumnKind.Done))).IsSuccess.Should().BeTrue();

        (await Move(task.Id, Kind(project, ColumnKind.Backlog))).Error.Should().Be(TaskErrors.CannotMoveBack);
        await SignInWith(Permissions.ProjectsManage);
        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).Error.Should().Be(ProjectErrors.MemberRequired);
        SignIn(owner);
        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).Error.Should().BeOfType<ValidationError>();
    }

    [Fact]
    public async Task Create_WithAnEstimate_KeepsItWithoutHistory()
    {
        var (_, _, task) = await ATaskInBacklog(estimatedMinutes: 90);

        task.EstimatedMinutes.Should().Be(90);
        (await Stored(task.Id))!.History.Should().BeEmpty();
    }

    [Theory]
    [InlineData(14)]
    [InlineData(60_001)]
    public async Task Create_WithAnEstimateOutOfItsLimits_ReturnsAValidationErrorOnEstimatedMinutes(int minutes)
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        SignIn(owner);

        (await Create(NewTask(project) with { EstimatedMinutes = minutes })).Error
            .Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("EstimatedMinutes");
        (await TasksOf(project)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetEstimate_AnyTime_ChangesItWithItsHistory()
    {
        var (_, project, task) = await ATaskInBacklog(estimatedMinutes: 60);
        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).IsSuccess.Should().BeTrue();

        var result = await Estimate(task.Id, 480);

        result.Value.EstimatedMinutes.Should().Be(480);
        Entries((await Stored(task.Id))!).Should().Contain((TaskHistoryFields.EstimatedMinutes, "60", "480"));
    }

    [Fact]
    public async Task SetEstimate_ToNothingBeforeInProgress_TakesItAway()
    {
        var (_, project, task) = await ATaskInBacklog(estimatedMinutes: 60);
        (await Move(task.Id, Kind(project, ColumnKind.Paused))).IsSuccess.Should().BeTrue();

        (await Estimate(task.Id, null)).Value.EstimatedMinutes.Should().BeNull();
    }

    // Once a task entered În lucru it keeps an estimate, wherever it went after (SPEC section 4.4).
    [Theory]
    [InlineData(ColumnKind.InProgress)]
    [InlineData(ColumnKind.Paused)]
    [InlineData(ColumnKind.Done)]
    public async Task SetEstimate_ToNothingOnceItEnteredInProgress_ReturnsAValidationErrorOnEstimatedMinutes(ColumnKind now)
    {
        var (_, project, task) = await ATaskInBacklog(estimatedMinutes: 60);
        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).IsSuccess.Should().BeTrue();
        if (now != ColumnKind.InProgress)
        {
            (await Move(task.Id, Kind(project, now))).IsSuccess.Should().BeTrue();
        }

        (await Estimate(task.Id, null)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("EstimatedMinutes");
        (await Stored(task.Id))!.EstimatedMinutes.Should().Be(60);
    }

    // The migration put tasks in În lucru without a move in their history: they count as started.
    [Fact]
    public async Task SetEstimate_ToNothingOfATaskInInProgressWithoutAMove_ReturnsAValidationError()
    {
        var owner = await CreateUser();
        var project = await CreateProject(builder => builder.OwnedBy(owner));
        var task = new TaskItemBuilder().InProject(project).InColumn(Kind(project, ColumnKind.InProgress)).Build();
        await AddTasks(project, task);
        SignIn(owner);
        (await Estimate(task.Id, 60)).IsSuccess.Should().BeTrue();

        (await Estimate(task.Id, null)).Error.Should().BeOfType<ValidationError>();
    }

    [Theory]
    [InlineData(14)]
    [InlineData(60_001)]
    public async Task SetEstimate_OutOfItsLimits_ReturnsAValidationErrorOnEstimatedMinutes(int minutes)
    {
        var (_, _, task) = await ATaskInBacklog();

        (await Estimate(task.Id, minutes)).Error.Should().BeOfType<ValidationError>().Which.Errors.Should().ContainKey("EstimatedMinutes");
    }

    [Fact]
    public async Task SetEstimate_ByOneWhoSeesTheProjectThroughAPermissionOnly_ReturnsMemberRequired()
    {
        var (_, _, task) = await ATaskInBacklog();
        await SignInWith(Permissions.ProjectsViewAll);

        (await Estimate(task.Id, 60)).Error.Should().Be(ProjectErrors.MemberRequired);
    }

    [Fact]
    public async Task SetEstimate_InAnArchivedProject_ReturnsArchived()
    {
        var (owner, project, task) = await ATaskInBacklog();
        await Archive(project, owner);

        (await Estimate(task.Id, 60)).Error.Should().Be(ProjectErrors.Archived);
    }

    // SPEC section 5.3: two changes at once, the second changes nothing.
    [Fact]
    public async Task SetEstimate_TwoAtOnce_GivesTheSecondChangedMeanwhile()
    {
        var (owner, _, task) = await ATaskInBacklog(estimatedMinutes: 60);

        var result = await PausedAtClockCall(
            1,
            () => HandleInOwnScope<SetTaskEstimateCommand, TaskItemDto>(new SetTaskEstimateCommand(task.Id, 120), owner),
            async () => (await HandleInOwnScope<SetTaskEstimateCommand, TaskItemDto>(new SetTaskEstimateCommand(task.Id, 180), owner)).IsSuccess.Should().BeTrue());

        result.Error.Should().Be(TaskErrors.ChangedMeanwhile);
        (await Stored(task.Id))!.EstimatedMinutes.Should().Be(180);
    }
}
