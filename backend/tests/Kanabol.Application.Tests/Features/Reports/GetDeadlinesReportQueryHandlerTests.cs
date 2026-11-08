using Kanabol.Application.Common;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetDeadlinesReport;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// GET /api/reports/deadlines (Tasks.md group 18; SPEC section 5.1.1): of the tasks finished in the interval
/// with a deadline, those finished by it and those after it, the rate of the first, and their periods; and
/// the tasks overdue now, open and not archived, whatever the interval, on the filter's project and person.
/// Now is Thursday 5 November 2026 at noon.
/// </summary>
public sealed class GetDeadlinesReportQueryHandlerTests(PostgresFixture postgres) : ReportsHandlerTest(postgres)
{
    private Task<Result<DeadlinesReportDto>> Deadlines(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? projectId = null,
        Guid? userId = null,
        ReportGranularity granularity = ReportGranularity.Week) =>
        Ask<GetDeadlinesReportQuery, DeadlinesReportDto>(new GetDeadlinesReportQuery(from, to, projectId, userId, granularity));

    private static string Numbers(DeadlinesReportDto report) =>
        $"{report.Overdue} {report.CompletedOnTime} {report.CompletedLate} {report.OnTimeRate?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "null"}";

    private static string Series(DeadlinesReportDto report) =>
        string.Join(' ', report.Series.Select(period => $"{period.PeriodStart:MM-dd}:{period.OnTime}/{period.Late}"));

    // On time: KAN-1 and WEB-1. Late: KAN-2 and KAN-7. Without a deadline: KAN-3, WEB-2, OLD-1. Overdue now:
    // KAN-5 and WEB-3; KAN-4 is archived, and OLD-2 is in an archived project.
    [Fact]
    public async Task Deadlines_OfTheFinishedTasks_CountsThoseOnTimeAndThoseLate()
    {
        await FinishedWork();

        Numbers((await Deadlines(From, To)).Value).Should().Be("2 2 2 0.5");
    }

    [Fact]
    public async Task Deadlines_ByTheWeek_PutsEachFinishedTaskInItsPeriod()
    {
        await FinishedWork();

        Series((await Deadlines(From, To)).Value).Should().Be("10-05:1/1 10-12:0/1 10-19:0/0 10-26:1/0");
    }

    [Fact]
    public async Task Deadlines_ByTheMonth_StepsACalendarMonth()
    {
        await FinishedWork();

        var report = (await Deadlines(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
            granularity: ReportGranularity.Month)).Value;

        Series(report).Should().Be("09-01:0/0 10-01:2/2");
    }

    // The tasks overdue are the state of today, not of the interval.
    [Fact]
    public async Task Deadlines_InAnotherInterval_CountsTheSameOverdueTasks()
    {
        await FinishedWork();

        var report = (await Deadlines(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero))).Value;

        Numbers(report).Should().Be("2 0 0 null");
    }

    [Fact]
    public async Task Deadlines_ForAProject_CountsOnlyItsTasks()
    {
        var team = await FinishedWork();

        Numbers((await Deadlines(From, To, team.Kan.Id)).Value).Should().Be("1 1 2 0.333");
    }

    [Fact]
    public async Task Deadlines_ForAUser_CountsOnlyTheirTasks()
    {
        var team = await FinishedWork();

        Numbers((await Deadlines(From, To, userId: team.Eva.Id)).Value).Should().Be("1 1 0 1");
        Numbers((await Deadlines(From, To, userId: team.Dan.Id)).Value).Should().Be("1 1 2 0.333");
    }

    // An archived project has nothing in progress, so nothing overdue now; what was finished in it counts.
    [Fact]
    public async Task Deadlines_ForAnArchivedProject_HasNothingOverdue()
    {
        var team = await FinishedWork();

        Numbers((await Deadlines(From, To, team.Old.Id)).Value).Should().Be("0 0 0 null");
    }

    [Fact]
    public async Task Deadlines_WithoutFinishedTasksWithADeadline_HasNoRate()
    {
        var team = await FinishedWork();

        var report = (await Deadlines(From, To, team.Web.Id, team.Dan.Id)).Value;

        report.OnTimeRate.Should().BeNull();
        Series(report).Should().Be("10-05:0/0 10-12:0/0 10-19:0/0 10-26:0/0");
    }

    [Fact]
    public async Task Deadlines_OfATaskFinishedAtItsDeadline_CountsItOnTime()
    {
        var team = await FinishedWork();
        await AddTasks(team.Web, Through(
            new TaskItemBuilder().InProject(team.Web).CreatedBy(team.Ana).WithTitle("La fix").AssignedTo(team.Eva).DueAt(October(21)),
            October(19),
            team.Ana,
            (DoneColumn(team.Web), October(21))));

        Numbers((await Deadlines(From, To, team.Web.Id)).Value).Should().Be("1 2 0 1");
    }

    [Fact]
    public async Task Deadlines_OfATaskFinishedASecondAfterItsDeadline_CountsItLate()
    {
        var team = await FinishedWork();
        await AddTasks(team.Web, Through(
            new TaskItemBuilder().InProject(team.Web).CreatedBy(team.Ana).WithTitle("O secundă").AssignedTo(team.Eva).DueAt(October(21)),
            October(19),
            team.Ana,
            (DoneColumn(team.Web), October(21).AddSeconds(1))));

        Numbers((await Deadlines(From, To, team.Web.Id)).Value).Should().Be("1 1 1 0.5");
    }

    [Fact]
    public async Task Deadlines_OfAnOpenTaskDueLater_DoesNotCountItOverdue()
    {
        var team = await FinishedWork();
        await AddTasks(team.Web, Through(
            new TaskItemBuilder().InProject(team.Web).CreatedBy(team.Ana).WithTitle("Mai târziu").AssignedTo(team.Eva).DueAt(Now.AddMinutes(1)),
            October(19),
            team.Ana));

        (await Deadlines(From, To, team.Web.Id)).Value.Overdue.Should().Be(1);
    }

    [Fact]
    public async Task Deadlines_ForAProjectThatDoesNotExist_ReturnsProjectNotFound()
    {
        await FinishedWork();

        (await Deadlines(From, To, Guid.CreateVersion7())).Error.Code.Should().Be("Project.NotFound");
    }

    [Fact]
    public async Task Deadlines_WithToBeforeFrom_ReturnsAValidationErrorOnTo()
    {
        await FinishedWork();

        (await Deadlines(To, From)).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("To");
    }

    [Fact]
    public async Task Deadlines_WithAGranularityThatDoesNotExist_ReturnsAValidationErrorOnGranularity()
    {
        await FinishedWork();

        (await Deadlines(From, To, granularity: (ReportGranularity)9)).Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Keys.Should().Equal("Granularity");
    }
}
