using System.Globalization;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Reports;
using Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;
using Kanabol.Tests.Shared.Builders;
using Kanabol.Tests.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Tests.Features.Reports;

/// <summary>
/// GET /api/reports/cycle-time (Tasks.md groups 18 and 22; SPEC section 5.1.1): on the tasks the completed
/// report counts, lead time from created_at to completed_at, cycle time from the first entry into În lucru,
/// or from created_at for a task that never entered it, each as an average and a median in whole seconds;
/// and with a project, the time in each of its columns, from the ColumnId entries of the history.
/// </summary>
public sealed class GetCycleTimeReportQueryHandlerTests(PostgresFixture postgres) : ReportsHandlerTest(postgres)
{
    private const long Hour = 3600;
    private const long Day = 24 * Hour;

    private Task<Result<CycleTimeReportDto>> CycleTime(DateTimeOffset? from, DateTimeOffset? to, Guid? projectId = null, Guid? userId = null) =>
        Ask<GetCycleTimeReportQuery, CycleTimeReportDto>(new GetCycleTimeReportQuery(from, to, projectId, userId));

    private static string PerColumn(CycleTimeReportDto report) =>
        string.Join(", ", report.PerColumn!.Select(column => $"{column.Name} {column.AverageSeconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{column.MedianSeconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}"));

    /// <summary>A project of its own, Ana's, so a test's few tasks are all its report holds.</summary>
    private async Task<(User Ana, Project Project)> AProjectOfItsOwn(Team team, string key)
    {
        var project = await CreateProject(builder => builder.WithKey(key).WithName(key).OwnedBy(team.Ana).WithMember(team.Dan));
        return (team.Ana, project);
    }

    [Fact]
    public async Task CycleTime_OfTheFinishedTasks_GivesTheirCount()
    {
        await FinishedWork();

        (await CycleTime(From, To)).Value.Count.Should().Be(7);
    }

    // KAN-1 7 d 1 h, KAN-2 12 d, KAN-3 5 d 2 h, KAN-7 3 d 14 h, WEB-1 7 d, WEB-2 1 d, OLD-1 5 d.
    [Fact]
    public async Task CycleTime_LeadTime_GoesFromCreationToCompletion()
    {
        await FinishedWork();

        var lead = (await CycleTime(From, To)).Value.LeadTime;

        // 3,517,200 seconds over 7, and the fourth of them in order, KAN-3.
        lead.Should().Be(new ReportDurationDto(AverageSeconds: 502_457, MedianSeconds: (5 * Day) + (2 * Hour)));
    }

    // KAN-1 2 d, KAN-2 12 d (created in În lucru), KAN-3 2 d, KAN-7 1 d 14 h, WEB-1 5 d, OLD-1 3 d, and WEB-2,
    // from Backlog straight to Gata, never in În lucru: 1 d, from its creation.
    [Fact]
    public async Task CycleTime_StartsAtTheFirstEntryIntoInProgress()
    {
        await FinishedWork();

        var cycle = (await CycleTime(From, To)).Value.CycleTime;

        // 2,296,800 seconds over 7, and 2 days in the middle.
        cycle.Should().Be(new ReportDurationDto(AverageSeconds: 328_114, MedianSeconds: 2 * Day));
    }

    // Through a column of the project's own first, then În lucru, a pause and back: from the first entry into În lucru.
    [Fact]
    public async Task CycleTime_ThroughAColumnOfItsOwnAndAPause_StartsAtTheFirstEntryIntoInProgress()
    {
        var team = await FinishedWork();
        var (ana, project) = await AProjectOfItsOwn(team, "ANA");
        var analysis = project.AddColumn("Analiză");
        await using (var db = Postgres.CreateContext())
        {
            db.BoardColumns.Add(analysis);
            await db.SaveChangesAsync();
        }

        var inProgress = project.Columns.Single(column => column.Kind == ColumnKind.InProgress);
        var paused = project.Columns.Single(column => column.Kind == ColumnKind.Paused);
        var task = new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle("Prin analiză").Build();
        task.CreatedAt = October(5);
        task.MoveTo(analysis, 0, ana.Id, October(6), hours: Hours);
        task.MoveTo(inProgress, 0, ana.Id, October(8), hours: Hours);
        task.MoveTo(paused, 0, ana.Id, October(9), "Aștept", Hours);
        task.MoveTo(inProgress, 0, ana.Id, October(12), hours: Hours);
        task.MoveTo(DoneColumn(project), 0, ana.Id, October(13), hours: Hours);
        await AddTasks(project, task);

        var report = (await CycleTime(From, To, project.Id)).Value;

        (report.LeadTime.AverageSeconds, report.CycleTime.AverageSeconds).Should().Be((8 * Day, 5 * Day));
    }

    [Fact]
    public async Task CycleTime_OfATaskCreatedInTheSecondColumn_StartsAtItsCreation()
    {
        var team = await FinishedWork();

        // KAN-2 alone: created in În lucru on 2 October at 09:00, finished on the 14th at 09:00.
        var report = (await CycleTime(October(14, 0), October(15, 0), team.Kan.Id)).Value;

        (report.Count, report.CycleTime, report.LeadTime).Should().Be((1, new ReportDurationDto(12 * Day, 12 * Day), new ReportDurationDto(12 * Day, 12 * Day)));
    }

    [Fact]
    public async Task CycleTime_WithoutAProject_HasNoTimeByColumn()
    {
        await FinishedWork();

        (await CycleTime(From, To)).Value.PerColumn.Should().BeNull();
    }

    // KAN-1: De făcut 5 d 1 h, În lucru 2 d; KAN-2: În lucru 12 d; KAN-3: De făcut 3 d 2 h, În lucru 2 d;
    // KAN-7: De făcut 2 d, În lucru 1 d 14 h. Each ends in Gata, the moment it was finished.
    [Fact]
    public async Task CycleTime_ForAProject_GivesTheTimeInEachColumnInTheBoardsOrder()
    {
        var team = await FinishedWork();

        var report = (await CycleTime(From, To, team.Kan.Id)).Value;

        PerColumn(report).Should().Be($"Backlog 291600/{(3 * Day) + (2 * Hour)}, În lucru 379800/{2 * Day}, În pauză -/-, Gata 0/0");
        report.PerColumn!.Select(column => column.ColumnId).Should().Equal(team.Kan.Columns.OrderBy(column => column.Position).Select(column => column.Id));
    }

    [Fact]
    public async Task CycleTime_ForAProject_CountsOnlyItsTasks()
    {
        var team = await FinishedWork();

        var report = (await CycleTime(From, To, team.Kan.Id)).Value;

        // KAN-1, KAN-2, KAN-3 and KAN-7: leads of 7 d 1 h, 12 d, 5 d 2 h and 3 d 14 h; the median between the middle two.
        report.Count.Should().Be(4);
        report.LeadTime.Should().Be(new ReportDurationDto(598_500, ((5 * Day) + (2 * Hour) + (7 * Day) + Hour) / 2));
        report.CycleTime.Should().Be(new ReportDurationDto(379_800, 2 * Day));
    }

    [Fact]
    public async Task CycleTime_ForAUser_TakesOnlyTheirTasks()
    {
        var team = await FinishedWork();

        var report = (await CycleTime(From, To, userId: team.Eva.Id)).Value;

        (report.Count, report.LeadTime, report.CycleTime).Should().Be((1, new ReportDurationDto(7 * Day, 7 * Day), new ReportDurationDto(5 * Day, 5 * Day)));
    }

    [Fact]
    public async Task CycleTime_WithoutFinishedTasks_HasNoTimes()
    {
        var team = await FinishedWork();

        var report = (await CycleTime(From, To, team.Web.Id, team.Ana.Id)).Value;

        report.Count.Should().Be(0);
        report.LeadTime.Should().Be(new ReportDurationDto(null, null));
        report.CycleTime.Should().Be(new ReportDurationDto(null, null));
        PerColumn(report).Should().Be("Backlog -/-, În lucru -/-, În pauză -/-, Gata -/-");
    }

    // Done on the 6th, back in În lucru on the 7th, done again on the 20th: it was finished on the 20th.
    [Fact]
    public async Task CycleTime_OfATaskReopened_CountsEachStayUntilItWasLastFinished()
    {
        var team = await FinishedWork();
        var (ana, project) = await AProjectOfItsOwn(team, "RED");
        await AddTasks(project, Through(
            new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle("Redeschis"),
            October(5),
            ana,
            (DoneColumn(project), October(6)),
            (ColumnAt(project, 1), October(7)),
            (DoneColumn(project), October(20))));

        var report = (await CycleTime(From, To, project.Id)).Value;

        (report.LeadTime.AverageSeconds, report.CycleTime.AverageSeconds).Should().Be((15 * Day, 13 * Day));
        PerColumn(report).Should().Be($"Backlog {Day}/{Day}, În lucru {13 * Day}/{13 * Day}, În pauză -/-, Gata {Day}/{Day}");
    }

    [Fact]
    public async Task CycleTime_OfAColumnDeletedSince_LeavesItOut()
    {
        var team = await FinishedWork();
        var (ana, project) = await AProjectOfItsOwn(team, "REV");
        var review = project.AddColumn("Review");
        await using (var db = Postgres.CreateContext())
        {
            db.BoardColumns.Add(review);
            await db.SaveChangesAsync();
        }

        await AddTasks(project, Through(
            new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle("Prin review"),
            October(5),
            ana,
            (review, October(6)),
            (DoneColumn(project), October(9))));
        await using (var db = Postgres.CreateContext())
        {
            await db.BoardColumns.Where(column => column.Id == review.Id).ExecuteDeleteAsync();
        }

        var report = (await CycleTime(From, To, project.Id)).Value;

        PerColumn(report).Should().Be($"Backlog {Day}/{Day}, În lucru -/-, În pauză -/-, Gata 0/0");
        report.CycleTime.AverageSeconds.Should().Be(4 * Day, "it never entered În lucru, so from its creation");
    }

    // A task finished without a move, as when its column became a done column: no exit, so from its creation.
    [Fact]
    public async Task CycleTime_OfATaskFinishedInTheFirstColumn_StartsAtItsCreation()
    {
        var team = await FinishedWork();
        var (ana, project) = await AProjectOfItsOwn(team, "SOL");
        var task = Through(new TaskItemBuilder().InProject(project).CreatedBy(ana).WithTitle("Pe loc"), October(5), ana);
        await AddTasks(project, task);
        await using (var db = Postgres.CreateContext())
        {
            await db.TaskItems.Where(stored => stored.Id == task.Id).ExecuteUpdateAsync(set => set.SetProperty(stored => stored.CompletedAt, October(8)));
        }

        var report = (await CycleTime(From, To, project.Id)).Value;

        (report.LeadTime.AverageSeconds, report.CycleTime.AverageSeconds).Should().Be((3 * Day, 3 * Day));
        PerColumn(report).Should().Be($"Backlog {3 * Day}/{3 * Day}, În lucru -/-, În pauză -/-, Gata -/-");
    }

    [Fact]
    public async Task CycleTime_OfAnArchivedProject_IsReported()
    {
        var team = await FinishedWork();

        var report = (await CycleTime(From, To, team.Old.Id)).Value;

        (report.Count, report.CycleTime.AverageSeconds).Should().Be((1, 3 * Day));
    }

    [Fact]
    public async Task CycleTime_ForAProjectThatDoesNotExist_ReturnsProjectNotFound()
    {
        await FinishedWork();

        (await CycleTime(From, To, Guid.CreateVersion7())).Error.Code.Should().Be("Project.NotFound");
    }

    [Fact]
    public async Task CycleTime_WithoutFrom_ReturnsAValidationErrorOnFrom()
    {
        await FinishedWork();

        (await CycleTime(null, To)).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("From");
    }

    [Fact]
    public async Task CycleTime_Over366Days_ReturnsAValidationErrorOnTo()
    {
        await FinishedWork();

        (await CycleTime(From, From.AddDays(367))).Error.Should().BeOfType<ValidationError>().Which.Errors.Keys.Should().Equal("To");
    }
}
