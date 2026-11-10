using Kanabol.Domain.Projects;
using Kanabol.Domain.Settings;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// The worked time's counter (SPEC section 4.4, "Timpul lucrat", group 22): it runs only while the task is in În
/// lucru and not archived. Entering, by a move or a restore, starts it; leaving, by a move or the archive, adds
/// the schedule's seconds since then. It writes no history. Wednesday 11 November 2026 is a working day, 08:30 to
/// 16:30 in Bucharest, 06:30 to 14:30 UTC.
/// </summary>
public sealed class TaskWorkedTimeTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly DateTimeOffset _nine = new(2026, 11, 11, 7, 0, 0, TimeSpan.Zero);
    private static readonly WorkingHours _hours = new(WorkSchedule.Default(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest"));

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn Kind(ColumnKind kind) => _project.Columns.Single(column => column.Kind == kind);

    private TaskItem NewTask() => new TaskItemBuilder().InProject(_project).Build();

    private static DateTimeOffset At(int hours, int minutes = 0) => _nine.AddHours(hours - 9).AddMinutes(minutes);

    [Fact]
    public void ANewTask_HasWorkedNothingAndIsNotWorking()
    {
        var task = NewTask();

        (task.WorkedSeconds, task.WorkStartedAt, task.IsWorking).Should().Be((0, null, false));
    }

    [Fact]
    public void MoveTo_InProgress_StartsTheCounter()
    {
        var task = NewTask();

        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);

        (task.WorkStartedAt, task.IsWorking, task.WorkedSeconds).Should().Be((At(9), true, 0));
    }

    [Fact]
    public void MoveTo_OutOfInProgress_AddsTheScheduleSecondsAndStops()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);

        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, At(11, 30), "Aștept", _hours);

        (task.WorkedSeconds, task.WorkStartedAt, task.IsWorking).Should().Be((150 * 60, null, false));
        task.History.Should().OnlyContain(entry => entry.Field == TaskHistoryFields.ColumnId || entry.Field == TaskHistoryFields.PauseReason);
    }

    // Back in În lucru, it goes on from where it stopped; the evening after 16:30 counts nothing.
    [Fact]
    public void MoveTo_InProgressAgain_GoesOnFromWhereItStopped()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);
        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, At(10), "Aștept", _hours);

        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(16), hours: _hours);
        task.MoveTo(Kind(ColumnKind.Done), 0, _by, At(20), hours: _hours);

        task.WorkedSeconds.Should().Be((60 + 30) * 60);
        task.IsWorking.Should().BeFalse();
    }

    [Fact]
    public void MoveTo_WithinInProgress_KeepsItRunning()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);

        task.MoveTo(Kind(ColumnKind.InProgress), 3, _by, At(10), hours: _hours);

        (task.WorkStartedAt, task.WorkedSeconds).Should().Be((At(9), 0));
    }

    [Fact]
    public void MoveTo_BetweenOtherColumns_TouchesNothing()
    {
        var task = NewTask();

        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, At(9), "Aștept");
        task.MoveTo(Kind(ColumnKind.Done), 0, _by, At(10));

        (task.WorkedSeconds, task.WorkStartedAt).Should().Be((0, null));
    }

    [Fact]
    public void MoveTo_OutOfInProgressWithoutTheWorkingHours_Throws()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);

        var move = () => task.MoveTo(Kind(ColumnKind.Done), 0, _by, At(10));

        move.Should().Throw<ArgumentNullException>();
        task.ColumnId.Should().Be(Kind(ColumnKind.InProgress).Id);
    }

    [Fact]
    public void Archive_InProgress_AddsTheSecondsAndStops()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);

        task.Archive(_by, At(10, 15), _hours);

        (task.WorkedSeconds, task.WorkStartedAt).Should().Be((75 * 60, null));
    }

    [Fact]
    public void Restore_IntoInProgress_StartsItAgain_AndElsewhereNot()
    {
        var inProgress = NewTask();
        inProgress.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);
        inProgress.Archive(_by, At(10), _hours);
        var paused = NewTask();
        paused.MoveTo(Kind(ColumnKind.Paused), 0, _by, At(9), "Aștept");
        paused.Archive(_by, At(10));

        inProgress.Restore(_by, At(13), Kind(ColumnKind.InProgress));
        paused.Restore(_by, At(13), Kind(ColumnKind.Paused));

        (inProgress.WorkStartedAt, inProgress.WorkedSeconds).Should().Be((At(13), 3600));
        paused.WorkStartedAt.Should().BeNull();
        paused.PauseReason.Should().Be("Aștept");
    }

    [Fact]
    public void Restore_WithAnotherColumnThanItsOwn_Throws()
    {
        var task = NewTask();
        task.Archive(_by, At(10));

        var restore = () => task.Restore(_by, At(11), Kind(ColumnKind.InProgress));

        restore.Should().Throw<ArgumentException>();
    }

    // The open interval counts up to now, as the DTOs read it, without changing the task.
    [Fact]
    public void WorkedSecondsAt_WhileWorking_CountsTheOpenIntervalToNow()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(9), hours: _hours);
        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, At(10), "Aștept", _hours);
        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, At(14), hours: _hours);

        task.WorkedSecondsAt(At(15), _hours).Should().Be(2 * 3600);
        task.WorkedSeconds.Should().Be(3600);
        NewTask().WorkedSecondsAt(At(15), _hours).Should().Be(0);
    }
}
