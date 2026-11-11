using Kanabol.Application.Common;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetWorkTimeReport;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// GET /api/reports/work-time (SPEC section 5.1.1, group 22): on the tasks the completed report counts, the
/// estimate against the worked time, which counts the schedule's hours only, here the default one in UTC,
/// Monday to Friday 08:30 to 16:30. Only the tasks with an estimate that were worked go into the sums.
/// Every task is created on 20 September. WTM, Ana's, with Dan: WTM-1 Dan's, 2 h estimated, 4 h worked on Monday
/// 5 October; WTM-2 Dan's and Ana's,
/// 8 h estimated, 2 h worked on the 6th, archived after; WTM-3 Ana's, no estimate, 1 h on the 7th; WTM-4
/// nobody's, 1 h estimated, finished from Backlog on the 8th without work; WTM-5 Ana's, 1 h estimated,
/// Friday 9 October 15:00 to Monday 12 at 09:30, 2 h 30 of the schedule; WTM-6 Dan's, open in În lucru;
/// WTM-7 Dan's, finished in September. ALT, Ana's, with Dan: ALT-1 Dan's, 1 h estimated, 1 h worked on the 13th.
/// </summary>
public sealed class GetWorkTimeReportQueryHandlerTests(PostgresFixture postgres) : ReportsHandlerTest(postgres)
{
    private const long Hour = 3600;

    private sealed record Work(User Ana, User Dan, Project Wtm, Project Alt, TaskItem[] WtmTasks, TaskItem[] AltTasks);

    private Task<Result<WorkTimeReportDto>> WorkTime(Guid? projectId = null, Guid? userId = null) =>
        Ask<GetWorkTimeReportQuery, WorkTimeReportDto>(new GetWorkTimeReportQuery(From, To, projectId, userId));

    private static BoardColumn Kind(Project project, ColumnKind kind) => project.Columns.Single(column => column.Kind == kind);

    private async Task<Work> WorkDone()
    {
        var ana = await CreatePerson("Ana Pop", "ana.pop@example.com");
        var dan = await CreatePerson("Dan Ionescu", "dan@example.com");
        var wtm = await CreateProject(builder => builder.WithKey("WTM").WithName("Timp").OwnedBy(ana).WithMember(dan));
        var alt = await CreateProject(builder => builder.WithKey("ALT").WithName("Altul").OwnedBy(ana).WithMember(dan));

        TaskItem Made(Project project, string title, int? estimate, User[] people, params (ColumnKind Kind, DateTimeOffset At)[] moves) =>
            Moved(new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle(title).AssignedTo(people), project, ana, estimate, moves);

        TaskItem[] wtmTasks =
        [
            Made(wtm, "Peste estimare", 120, [dan], (ColumnKind.InProgress, October(5, 9)), (ColumnKind.Done, October(5, 13))),
            Made(wtm, "Sub estimare", 480, [dan, ana], (ColumnKind.InProgress, October(6, 9)), (ColumnKind.Done, October(6, 11))),
            Made(wtm, "Fără estimare", null, [ana], (ColumnKind.InProgress, October(7, 9)), (ColumnKind.Done, October(7, 10))),
            Made(wtm, "Estimat, nelucrat", 60, [], (ColumnKind.Done, October(8))),
            Made(wtm, "Peste, peste weekend", 60, [ana], (ColumnKind.InProgress, October(9, 15)), (ColumnKind.Done, new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero))),
            Made(wtm, "Deschis", 30, [dan], (ColumnKind.InProgress, October(14))),
            Made(wtm, "Din septembrie", 30, [dan], (ColumnKind.InProgress, new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero)), (ColumnKind.Done, new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero))),
        ];
        wtmTasks[1].Archive(ana.Id, October(20), Hours);
        await AddTasks(wtm, wtmTasks);

        TaskItem[] altTasks = [Made(alt, "La fix", 60, [dan], (ColumnKind.InProgress, October(13, 10)), (ColumnKind.Done, October(13, 11)))];
        await AddTasks(alt, altTasks);

        await SignInWithReports();
        return new Work(ana, dan, wtm, alt, wtmTasks, altTasks);
    }

    private static TaskItem Moved(TaskItemBuilder builder, Project project, User by, int? estimate, (ColumnKind Kind, DateTimeOffset At)[] moves)
    {
        var task = builder.Build();
        task.CreatedAt = new DateTimeOffset(2026, 9, 20, 10, 0, 0, TimeSpan.Zero);
        if (estimate is { } minutes)
        {
            task.SetEstimate(minutes, by.Id, task.CreatedAt);
        }

        foreach (var (kind, at) in moves)
        {
            task.MoveTo(Kind(project, kind), 0, by.Id, at, hours: Hours);
        }

        return task;
    }

    [Fact]
    public async Task WorkTime_OfTheFinishedTasks_SumsTheEstimatedOnesThatWereWorked()
    {
        await WorkDone();

        var report = (await WorkTime()).Value;

        // WTM-1, 2, 5 and ALT-1: 2 h + 8 h + 1 h + 1 h estimated, 4 h + 2 h + 2 h 30 + 1 h worked.
        report.Should().BeEquivalentTo(new
        {
            Count = 6,
            EstimatedCount = 4,
            EstimatedSeconds = 12 * Hour,
            WorkedSeconds = (9 * Hour) + 1800,
            OverCount = 2,
        });
        report.Deviation.Should().BeApproximately(((9.5 * Hour) - (12 * Hour)) / (12 * Hour), 1e-9);
    }

    [Fact]
    public async Task WorkTime_ByUserAndByProject_TakesEachTaskWholeForEachOfItsAssignees()
    {
        var work = await WorkDone();

        var report = (await WorkTime()).Value;

        report.ByUser.Should().Equal(
            new ReportUserWorkTimeDto(work.Ana.Id, "Ana Pop", 3, 9 * Hour, (4 * Hour) + 1800),
            new ReportUserWorkTimeDto(work.Dan.Id, "Dan Ionescu", 3, 11 * Hour, 7 * Hour));
        report.ByProject.Should().Equal(
            new ReportProjectWorkTimeDto(work.Wtm.Id, "WTM", "Timp", 5, 11 * Hour, (8 * Hour) + 1800),
            new ReportProjectWorkTimeDto(work.Alt.Id, "ALT", "Altul", 1, Hour, Hour));
    }

    [Fact]
    public async Task WorkTime_Over_GivesTheTasksOverTheirEstimate_FromTheBiggest()
    {
        var work = await WorkDone();

        var over = (await WorkTime()).Value.Over;

        over.Should().Equal(
            new ReportOverrunDto(work.WtmTasks[0].Id, "WTM-1", "Peste estimare", 2 * Hour, 4 * Hour),
            new ReportOverrunDto(work.WtmTasks[4].Id, "WTM-5", "Peste, peste weekend", Hour, (2 * Hour) + 1800));
    }

    [Fact]
    public async Task WorkTime_Over_GivesAtMost20()
    {
        var ana = await CreatePerson("Ana Pop", "ana.pop@example.com");
        var project = await CreateProject(builder => builder.WithKey("MUL").OwnedBy(ana));
        await AddTasks(project, [.. Enumerable.Range(1, 22).Select(number => Moved(
            new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle($"Task {number}"),
            project,
            ana,
            15,
            [(ColumnKind.InProgress, October(5, 9)), (ColumnKind.Done, October(5, 9).AddMinutes(15 + number))]))]);
        await SignInWithReports();

        var report = (await WorkTime(project.Id)).Value;

        (report.OverCount, report.Over.Count).Should().Be((22, 20));
        report.Over[0].Title.Should().Be("Task 22");
        report.Over.Select(task => task.WorkedSeconds - task.EstimatedSeconds).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task WorkTime_ForAProject_CountsOnlyItsTasks()
    {
        var work = await WorkDone();

        var report = (await WorkTime(work.Alt.Id)).Value;

        report.Should().BeEquivalentTo(new { Count = 1, EstimatedCount = 1, EstimatedSeconds = Hour, WorkedSeconds = Hour, OverCount = 0, Deviation = 0.0 });
        report.Over.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkTime_ForAUser_TakesOnlyTheirTasks()
    {
        var work = await WorkDone();

        var report = (await WorkTime(userId: work.Dan.Id)).Value;

        report.Should().BeEquivalentTo(new { Count = 3, EstimatedCount = 3, EstimatedSeconds = 11 * Hour, WorkedSeconds = 7 * Hour, OverCount = 1 });
        report.ByUser.Select(person => person.UserId).Should().Equal(work.Dan.Id);
    }

    [Fact]
    public async Task WorkTime_WithoutAnEstimatedTaskWorked_HasNoDeviation()
    {
        var work = await WorkDone();

        // WTM-3 alone, on the 7th: worked, without an estimate.
        var report = (await Ask<GetWorkTimeReportQuery, WorkTimeReportDto>(new GetWorkTimeReportQuery(October(7, 0), October(8, 0), work.Wtm.Id, null))).Value;

        report.Should().BeEquivalentTo(new { Count = 1, EstimatedCount = 0, EstimatedSeconds = 0L, WorkedSeconds = 0L, OverCount = 0, Deviation = (double?)null });
    }

    [Fact]
    public async Task WorkTime_WithoutFrom_ReturnsAValidationErrorOnFrom()
    {
        await SignInWithReports();

        var result = await Ask<GetWorkTimeReportQuery, WorkTimeReportDto>(new GetWorkTimeReportQuery(null, To, null, null));

        result.Error.Code.Should().Be("Validation");
    }
}
