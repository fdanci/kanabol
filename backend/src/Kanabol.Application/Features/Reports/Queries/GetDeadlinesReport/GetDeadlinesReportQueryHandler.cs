using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Reports.Queries.GetDeadlinesReport;

/// <summary>
/// The deadlines (SPEC section 5.1.1): the tasks overdue now are counted in the database, on the filter's
/// project and person; the finished ones with a deadline are read, which the 366 days bound, to put them in
/// their periods. A task finished at its very deadline is on time.
/// </summary>
public sealed class GetDeadlinesReportQueryHandler(IAppDbContext db, ReportAccess access, IClock clock)
    : IQueryHandler<GetDeadlinesReportQuery, DeadlinesReportDto>
{
    public async Task<Result<DeadlinesReportDto>> Handle(GetDeadlinesReportQuery query, CancellationToken ct)
    {
        var allowed = await access.Check(query, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var now = clock.UtcNow;
        var periods = ReportPeriods.Of(query, query.Granularity);

        // As the alerts and the main panel have it: an archived project has nothing in progress. The project
        // filter is written out, since Tasks turns the query's filters off.
        var overdueNow = db.Tasks(includeArchived: false).Where(task =>
            task.CompletedAt == null
            && task.DueDate < now
            && db.Projects.IgnoreQueryFilters().Any(project => project.Id == task.ProjectId && project.ArchivedAt == null));
        var overdue = await db.Narrowed(overdueNow, query).CountAsync(ct);

        var finished = await db.CompletedIn(periods, query)
            .AsNoTracking()
            .Where(task => task.DueDate != null)
            .Select(task => new { CompletedAt = task.CompletedAt!.Value, DueDate = task.DueDate!.Value })
            .ToListAsync(ct);
        var onTime = finished.Where(task => task.CompletedAt <= task.DueDate).Select(task => task.CompletedAt).ToList();
        var late = finished.Where(task => task.CompletedAt > task.DueDate).Select(task => task.CompletedAt).ToList();

        var onTimeByPeriod = periods.Count(onTime);
        var lateByPeriod = periods.Count(late);
        return new DeadlinesReportDto(
            overdue,
            late.Count,
            onTime.Count,
            finished.Count == 0 ? null : (double)onTime.Count / finished.Count,
            [.. periods.Starts.Select((start, index) => new ReportDeadlinePeriodDto(start, onTimeByPeriod[index], lateByPeriod[index]))]);
    }
}
