using Kanabol.Application.Common;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetCompletedReport;
using Kanabol.Domain.Tasks;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// GET /api/reports/completed (Tasks.md group 18; SPEC section 5.1.1): the tasks with completed_at in
/// [from, to), archived after or not, in projects archived or not; counted once in total and by project,
/// once for each assignee by user, and in periods that start at from and step a week or a calendar month.
/// </summary>
public sealed class GetCompletedReportQueryHandlerTests(PostgresFixture postgres) : ReportsHandlerTest(postgres)
{
    private Task<Result<CompletedReportDto>> Completed(
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? projectId = null,
        Guid? userId = null,
        ReportGranularity granularity = ReportGranularity.Week) =>
        Ask<GetCompletedReportQuery, CompletedReportDto>(new GetCompletedReportQuery(from, to, projectId, userId, granularity));

    private static string ByProject(CompletedReportDto report) =>
        string.Join(' ', report.ByProject.Select(row => $"{row.Key}:{row.Count}"));

    private static string ByUser(CompletedReportDto report) =>
        string.Join(", ", report.ByUser.Select(row => $"{row.Name}:{row.Count}"));

    private static string Series(CompletedReportDto report) =>
        string.Join(' ', report.Series.Select(period => $"{period.PeriodStart:MM-dd}:{period.Count}"));

    [Fact]
    public async Task Completed_InSeveralProjects_CountsEachTaskFinishedInTheInterval()
    {
        await FinishedWork();

        var report = (await Completed(From, To)).Value;

        // KAN-1, KAN-2, KAN-3 (archived after), KAN-7 (at From), WEB-1, WEB-2 (nobody's), OLD-1 (archived project).
        report.Total.Should().Be(7);
        ByProject(report).Should().Be("KAN:4 WEB:2 OLD:1");
    }

    [Fact]
    public async Task Completed_WithSeveralAssignees_CountsTheTaskForEachOfThem()
    {
        await FinishedWork();

        var report = (await Completed(From, To)).Value;

        // Ana is on KAN-1 and KAN-3 with Dan; WEB-2, nobody's, is in no row. The sum goes past the total.
        ByUser(report).Should().Be("Dan Ionescu:5, Ana Pop:2, Eva Marin:1");
        report.ByUser.Sum(row => row.Count).Should().Be(8);
    }

    [Fact]
    public async Task Completed_ByUser_GivesEachPersonsIdAndName()
    {
        var team = await FinishedWork();

        var report = (await Completed(From, To)).Value;

        report.ByUser[0].Should().Be(new ReportUserCountDto(team.Dan.Id, "Dan Ionescu", 5));
        report.ByProject[0].Should().Be(new ReportProjectCountDto(team.Kan.Id, "KAN", "Kanabol", 4));
    }

    [Fact]
    public async Task Completed_ByTheWeek_HasPeriodsFromTheIntervalsStart()
    {
        await FinishedWork();

        var report = (await Completed(From, To)).Value;

        Series(report).Should().Be("10-05:2 10-12:2 10-19:1 10-26:2");
    }

    [Fact]
    public async Task Completed_ByTheWeek_StartsEachPeriodSevenDaysAfterTheOneBefore()
    {
        await FinishedWork();

        var report = (await Completed(From, To)).Value;

        report.Series.Select(period => period.PeriodStart).Should().Equal(From, From.AddDays(7), From.AddDays(14), From.AddDays(21));
    }

    [Fact]
    public async Task Completed_ByTheWeekFromAnotherDay_StartsThePeriodsThatDay()
    {
        await FinishedWork();

        // From Thursday 8 October at 10:00: KAN-1 at that very moment and KAN-2; OLD-1 at 10:00 on the 15th
        // and KAN-3; WEB-1 and WEB-2.
        var report = (await Completed(October(8), October(29))).Value;

        Series(report).Should().Be("10-08:2 10-15:2 10-22:2");
    }

    [Fact]
    public async Task Completed_WithAnIntervalThatIsNotWholeWeeks_EndsTheLastPeriodAtTo()
    {
        await FinishedWork();

        var report = (await Completed(From, October(20))).Value;

        // The third period runs from 19 October to 20 October 10:00, before KAN-3 at 12:00.
        Series(report).Should().Be("10-05:2 10-12:2 10-19:0");
        report.Total.Should().Be(4);
    }

    [Fact]
    public async Task Completed_ByTheMonth_StepsACalendarMonth()
    {
        await FinishedWork();

        var report = (await Completed(
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero),
            granularity: ReportGranularity.Month)).Value;

        // KAN-6 on 30 September; October's seven; KAN-8 at midnight on 2 November.
        Series(report).Should().Be("09-01:1 10-01:7 11-01:1");
    }

    [Fact]
    public async Task Completed_ByTheMonthFromTheLastDay_KeepsToTheEndOfEachMonth()
    {
        await FinishedWork();

        var report = (await Completed(
            new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero),
            granularity: ReportGranularity.Month)).Value;

        // 31 August, 30 September (no 31st), 31 October: from the start each time, not from the period before.
        report.Series.Select(period => period.PeriodStart.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Should().Equal("08-31", "09-30", "10-31");
    }

    // The browser sends midnight where the person is, with its offset: the months are theirs, not UTC's.
    [Fact]
    public async Task Completed_ByTheMonthWithAnOffset_StepsTheMonthsInThatOffset()
    {
        var team = await FinishedWork();
        var offset = TimeSpan.FromHours(3);

        // 01:30 on 1 October in Bucharest is still 30 September in UTC; 31 October 10:00 is October in both.
        TaskItemBuilder Dans(string title) => new TaskItemBuilder().InProject(team.Kan).CreatedBy(team.Ana).WithTitle(title).AssignedTo(team.Dan);
        await AddTasks(
            team.Kan,
            Through(Dans("După miezul nopții"), new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), team.Ana, (DoneColumn(team.Kan), new DateTimeOffset(2026, 9, 30, 22, 30, 0, TimeSpan.Zero))),
            Through(Dans("Sfârșit de lună"), October(29), team.Ana, (DoneColumn(team.Kan), October(31))));

        var report = (await Completed(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, offset),
            new DateTimeOffset(2026, 12, 1, 0, 0, 0, offset),
            granularity: ReportGranularity.Month)).Value;

        // A month stepped in UTC would start November at 21:00 on 30 October, and take the 31st into it.
        report.Series.Select(period => period.PeriodStart.ToString("O", System.Globalization.CultureInfo.InvariantCulture)).Should().Equal(
            "2026-10-01T00:00:00.0000000+03:00",
            "2026-11-01T00:00:00.0000000+03:00");
        report.Series.Select(period => period.Count).Should().Equal(9, 1);
    }

    // October in Bucharest starts at +03:00 and ends at +02:00: the browser sends both offsets.
    [Fact]
    public async Task Completed_ByTheMonthAcrossSummerTime_KeepsTheMonthOnePeriod()
    {
        await FinishedWork();

        var report = (await Completed(
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(2)),
            granularity: ReportGranularity.Month)).Value;

        report.Series.Select(period => period.Count).Should().Equal(7);
    }

    [Fact]
    public async Task Completed_ByTheWeekAcrossSummerTime_HasNoStrayHourAtTheEnd()
    {
        await FinishedWork();

        var report = (await Completed(
            new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.FromHours(2)))).Value;

        report.Series.Select(period => period.PeriodStart.Day).Should().Equal(5, 12, 19, 26);
    }

    [Fact]
    public async Task Completed_WithAnOffset_ComparesInstants()
    {
        await FinishedWork();
        var offset = TimeSpan.FromHours(2);

        var report = (await Completed(From.ToOffset(offset), To.ToOffset(offset))).Value;

        report.Total.Should().Be(7);
    }

    [Fact]
    public async Task Completed_ForAProject_CountsOnlyItsTasks()
    {
        var team = await FinishedWork();

        var report = (await Completed(From, To, projectId: team.Kan.Id)).Value;

        report.Total.Should().Be(4);
        ByProject(report).Should().Be("KAN:4");
        ByUser(report).Should().Be("Dan Ionescu:4, Ana Pop:2");
    }

    [Fact]
    public async Task Completed_ForAnArchivedProject_CountsItsTasks()
    {
        var team = await FinishedWork();

        (await Completed(From, To, projectId: team.Old.Id)).Value.Total.Should().Be(1);
    }

    [Fact]
    public async Task Completed_ForAUser_CountsOnlyTheTasksTheyAreAssignedTo()
    {
        var team = await FinishedWork();

        var report = (await Completed(From, To, userId: team.Dan.Id)).Value;

        report.Total.Should().Be(5);
        ByProject(report).Should().Be("KAN:4 OLD:1");
        ByUser(report).Should().Be("Dan Ionescu:5");
        Series(report).Should().Be("10-05:2 10-12:2 10-19:1 10-26:0");
    }

    [Fact]
    public async Task Completed_ForAUserInAProject_CountsTheirTasksThere()
    {
        var team = await FinishedWork();

        var report = (await Completed(From, To, team.Web.Id, team.Eva.Id)).Value;

        (report.Total, ByUser(report)).Should().Be((1, "Eva Marin:1"));
    }

    [Fact]
    public async Task Completed_ForSomeoneWithoutTasks_CountsNothingInEveryPeriod()
    {
        var team = await FinishedWork();

        var report = (await Completed(From, To, userId: team.Ana.Id, projectId: team.Web.Id)).Value;

        report.Total.Should().Be(0);
        report.ByUser.Should().BeEmpty();
        report.ByProject.Should().BeEmpty();
        Series(report).Should().Be("10-05:0 10-12:0 10-19:0 10-26:0");
    }

    [Fact]
    public async Task Completed_ForAProjectThatDoesNotExist_ReturnsProjectNotFound()
    {
        await FinishedWork();

        (await Completed(From, To, projectId: Guid.CreateVersion7())).Error.Code.Should().Be("Project.NotFound");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Completed_WithoutFromOrTo_ReturnsAValidationErrorOnIt(bool withFrom, bool withTo)
    {
        await FinishedWork();

        var errors = (await Completed(withFrom ? From : null, withTo ? To : null)).Error.Should().BeOfType<ValidationError>().Which.Errors;

        errors.Keys.Should().BeEquivalentTo(new[] { withFrom ? null : "From", withTo ? null : "To" }.OfType<string>());
    }

    [Fact]
    public async Task Completed_WithToAtFrom_ReturnsAValidationErrorOnTo()
    {
        await FinishedWork();

        (await Completed(From, From)).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("To");
    }

    [Fact]
    public async Task Completed_WithToBeforeFrom_ReturnsAValidationErrorOnTo()
    {
        await FinishedWork();

        (await Completed(To, From)).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("To");
    }

    // SPEC section 5.1.1: at most 366 days.
    [Fact]
    public async Task Completed_Over366Days_ReturnsAValidationErrorOnTo()
    {
        await FinishedWork();

        (await Completed(From, From.AddDays(366).AddTicks(1))).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("To");
    }

    [Fact]
    public async Task Completed_Over366DaysByTheMonth_IsAcceptedUpTo366Days()
    {
        await FinishedWork();

        var report = (await Completed(From, From.AddDays(366), granularity: ReportGranularity.Month)).Value;

        report.Series.Should().HaveCount(13);
        report.Total.Should().Be(8);
    }

    [Fact]
    public async Task Completed_With366DaysByTheWeek_HasFiftyThreePeriods()
    {
        await FinishedWork();

        (await Completed(From, From.AddDays(366))).Value.Series.Should().HaveCount(53);
    }

    [Fact]
    public async Task Completed_WithAGranularityThatDoesNotExist_ReturnsAValidationErrorOnGranularity()
    {
        await FinishedWork();

        (await Completed(From, To, granularity: (ReportGranularity)7)).Error.Should().BeOfType<ValidationError>()
            .Which.Errors.Keys.Should().Equal("Granularity");
    }

    [Fact]
    public async Task Completed_OfATaskReopenedAndFinishedAgain_CountsItWhenItWasLastFinished()
    {
        var team = await FinishedWork();
        await AddTasks(team.Web, Through(
            new TaskItemBuilder().InProject(team.Web).CreatedBy(team.Ana).WithTitle("Redeschis").AssignedTo(team.Eva),
            October(5),
            team.Ana,
            (DoneColumn(team.Web), October(6)),
            (ColumnAt(team.Web, 1), October(7)),
            (DoneColumn(team.Web), October(20))));

        var report = (await Completed(From, To, projectId: team.Web.Id)).Value;

        Series(report).Should().Be("10-05:0 10-12:0 10-19:1 10-26:2");
    }

    [Fact]
    public async Task Completed_OfATaskReopenedAndStillOpen_LeavesItOut()
    {
        var team = await FinishedWork();
        await AddTasks(team.Web, Through(
            new TaskItemBuilder().InProject(team.Web).CreatedBy(team.Ana).WithTitle("Încă deschis").AssignedTo(team.Eva),
            October(5),
            team.Ana,
            (DoneColumn(team.Web), October(6)),
            (ColumnAt(team.Web, 1), October(7))));

        (await Completed(From, To, projectId: team.Web.Id)).Value.Total.Should().Be(2);
    }
}
