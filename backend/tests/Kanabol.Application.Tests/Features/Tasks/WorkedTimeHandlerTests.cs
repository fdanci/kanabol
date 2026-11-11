using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Settings;
using Kanabol.Application.Features.Settings.Commands.SetWorkSchedule;
using Kanabol.Application.Features.Tasks;
using Kanabol.Application.Features.Tasks.Commands.ArchiveTask;
using Kanabol.Application.Features.Tasks.Commands.MoveTask;
using Kanabol.Application.Features.Tasks.Commands.RestoreTask;
using Kanabol.Application.Features.Tasks.Queries.GetBoard;
using Kanabol.Application.Features.Tasks.Queries.GetMyTasks;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Application.Features.Tasks.Queries.GetTaskById;
using Kanabol.Domain.Authorization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Database;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Kanabol.Application.Tests.Features.Tasks;

/// <summary>
/// The worked time (SPEC section 4.4, "Timpul lucrat", group 22) through the handlers: moves, the archive and the
/// restore drive the counter, in the schedule of the database and the zone of Schedule:TimeZone; every task DTO
/// gives estimatedMinutes, workedSeconds with the open interval up to now, isWorking and pauseReason. Now is
/// Thursday 5 November 2026, 12:00 UTC, 14:00 in Bucharest, two and a half hours before the end of the day.
/// </summary>
public sealed class WorkedTimeHandlerTests(PostgresFixture postgres) : TaskHandlerTest(postgres)
{
    private static BoardColumn Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    private void At(DateTimeOffset moment) => Clock.UtcNow.Returns(moment);

    private Task<Result<TaskItemDto>> Move(Guid taskId, BoardColumn column) =>
        Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(taskId, column.Id, null, 240, "Aștept clientul"));

    private Task<Result<TaskItemDto>> Read(Guid taskId) =>
        Ask<GetTaskByIdQuery, TaskItemDto>(new GetTaskByIdQuery(taskId, IncludeArchived: false));

    private async Task<(User Owner, Project Project, TaskItemDto Task)> ATask()
    {
        var owner = await CreateUser(permissions: [Permissions.TasksArchive, Permissions.SettingsManage]);
        var project = await CreateProject(builder => builder.WithKey("TIM").OwnedBy(owner));
        SignIn(owner);
        var task = (await Create(NewTask(project, "Login SSO", assigneeIds: [owner.Id]))).Value;
        return (owner, project, task);
    }

    [Fact]
    public async Task Move_IntoAndOutOfInProgress_CountsTheScheduleSecondsBetween()
    {
        var (_, project, task) = await ATask();
        (await Move(task.Id, Kind(project, ColumnKind.InProgress))).Value.Should().BeEquivalentTo(new { IsWorking = true, WorkedSeconds = 0L });

        At(Now.AddHours(2));
        var paused = (await Move(task.Id, Kind(project, ColumnKind.Paused))).Value;

        paused.Should().BeEquivalentTo(new { IsWorking = false, WorkedSeconds = 7200L, PauseReason = "Aștept clientul", EstimatedMinutes = 240 });
        var stored = (await Stored(task.Id))!;
        (stored.WorkedSeconds, stored.WorkStartedAt).Should().Be((7200, null));
        stored.History.Should().NotContain(entry => entry.Field == "WorkedSeconds" || entry.Field == "WorkStartedAt");
    }

    // SPEC section 4.4 (group 23, an explicit check): a task that has an estimate goes back into În lucru without
    // one and without being asked, keeps it, writes no new estimate, and its counter goes on from where it stopped.
    // 14:00 to 15:00 in Bucharest, on pause until 16:00, back until 16:30: 5400 seconds.
    [Fact]
    public async Task Move_BackIntoInProgressFromAPause_TakesNoEstimateAndTheCounterGoesOnFromWhereItStopped()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));
        At(Now.AddHours(1));
        await Move(task.Id, Kind(project, ColumnKind.Paused));
        At(Now.AddHours(2));

        var back = await Handle<MoveTaskCommand, TaskItemDto>(new MoveTaskCommand(task.Id, Kind(project, ColumnKind.InProgress).Id, null));

        back.Value.Should().BeEquivalentTo(new { EstimatedMinutes = 240, IsWorking = true, WorkedSeconds = 3600L, PauseReason = (string?)null });
        At(Now.AddHours(2.5));
        (await Read(task.Id)).Value.WorkedSeconds.Should().Be(5400);
        (await Move(task.Id, Kind(project, ColumnKind.Done))).Value.WorkedSeconds.Should().Be(5400);
        (await Stored(task.Id))!.History.Count(entry => entry.Field == "EstimatedMinutes").Should().Be(1);
    }

    // The evening after 16:30 and the night count nothing: Friday 6 November 09:00 adds the morning's half hour.
    [Fact]
    public async Task Move_OutOfInProgressTheNextMorning_CountsOnlyTheHoursOfTheSchedule()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));

        At(new DateTimeOffset(2026, 11, 6, 7, 0, 0, TimeSpan.Zero));
        var done = (await Move(task.Id, Kind(project, ColumnKind.Done))).Value;

        done.WorkedSeconds.Should().Be((150 + 30) * 60L);
    }

    [Fact]
    public async Task Read_WhileWorking_CountsTheOpenIntervalUpToNow()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));

        At(Now.AddMinutes(45));
        var read = (await Read(task.Id)).Value;

        read.Should().BeEquivalentTo(new { IsWorking = true, WorkedSeconds = 45 * 60L });
        (await Stored(task.Id))!.WorkedSeconds.Should().Be(0, "reading changes nothing");
    }

    [Fact]
    public async Task TheBoardTheListAndMyTasks_GiveTheEstimateTheWorkedTimeWorkingAndThePausesReason()
    {
        var (owner, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));
        At(Now.AddHours(1));
        var paused = (await Create(NewTask(project, "Pe pauză", assigneeIds: [owner.Id]))).Value;
        await Move(paused.Id, Kind(project, ColumnKind.Paused));
        At(Now.AddHours(2));

        var board = (await Ask<GetBoardQuery, BoardDto>(new GetBoardQuery(project.Id))).Value;
        var list = (await Ask<GetProjectTasksQuery, Page<TaskSummaryDto>>(new GetProjectTasksQuery(project.Id))).Value;
        var mine = (await Ask<GetMyTasksQuery, Page<MyTaskDto>>(new GetMyTasksQuery())).Value;

        object working = new { EstimatedMinutes = 240, WorkedSeconds = 7200L, IsWorking = true, PauseReason = (string?)null };
        object onPause = new { EstimatedMinutes = (int?)null, WorkedSeconds = 0L, IsWorking = false, PauseReason = "Aștept clientul" };
        board.Tasks.Single(card => card.Id == task.Id).Should().BeEquivalentTo(working);
        board.Tasks.Single(card => card.Id == paused.Id).Should().BeEquivalentTo(onPause);
        list.Items.Single(row => row.Id == task.Id).Should().BeEquivalentTo(working);
        list.Items.Single(row => row.Id == paused.Id).Should().BeEquivalentTo(onPause);
        mine.Items.Single(row => row.Id == task.Id).Should().BeEquivalentTo(working);
        mine.Items.Single(row => row.Id == paused.Id).Should().BeEquivalentTo(onPause);
    }

    [Fact]
    public async Task Archive_InProgress_StopsTheCounter_AndTheRestoreStartsItAgain()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));
        At(Now.AddHours(1));

        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();
        var archived = (await Stored(task.Id))!;
        (archived.WorkedSeconds, archived.WorkStartedAt).Should().Be((3600, null));

        At(Now.AddHours(2));
        var restored = (await Handle<RestoreTaskCommand, TaskItemDto>(new RestoreTaskCommand(task.Id))).Value;

        restored.Should().BeEquivalentTo(new { IsWorking = true, WorkedSeconds = 3600L, EstimatedMinutes = 240 });
        (await Stored(task.Id))!.WorkStartedAt.Should().Be(Now.AddHours(2));
    }

    [Fact]
    public async Task Restore_IntoAnotherColumn_LeavesTheCounterStoppedAndKeepsThePause()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.Paused));
        (await Handle<ArchiveTaskCommand, Unit>(new ArchiveTaskCommand(task.Id))).IsSuccess.Should().BeTrue();

        var restored = (await Handle<RestoreTaskCommand, TaskItemDto>(new RestoreTaskCommand(task.Id))).Value;

        restored.Should().BeEquivalentTo(new { IsWorking = false, WorkedSeconds = 0L, PauseReason = "Aștept clientul" });
    }

    // SPEC section 4.4: an open interval is counted with the schedule of the moment it's read, accepted so. The
    // schedule is read once per request, so the read comes in a scope of its own, as the next request would.
    [Fact]
    public async Task AScheduleChanged_WhileATaskWorks_CountsItsOpenIntervalWithTheNewHours()
    {
        var (_, project, task) = await ATask();
        await Move(task.Id, Kind(project, ColumnKind.InProgress));
        (await Handle<SetWorkScheduleCommand, WorkScheduleDto>(new SetWorkScheduleCommand([1, 2, 3, 4, 5], "08:30", "15:00"))).IsSuccess.Should().BeTrue();

        At(Now.AddHours(2));

        await using var request = NewScope();
        var read = await request.ServiceProvider.GetRequiredService<IQueryHandler<GetTaskByIdQuery, TaskItemDto>>()
            .Handle(new GetTaskByIdQuery(task.Id, IncludeArchived: false), CancellationToken.None);
        read.Value.WorkedSeconds.Should().Be(3600L, "14:00 to 15:00, where the day ends now");
    }
}
