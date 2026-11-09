using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Reports.Queries.GetCompletedReport;

/// <summary>
/// The tasks finished in the interval (SPEC section 5.1.1), counted in the database by project and by
/// assignee; the moments are read to put them in their periods, which the interval's 366 days bound.
/// </summary>
public sealed class GetCompletedReportQueryHandler(IAppDbContext db, ReportAccess access)
    : IQueryHandler<GetCompletedReportQuery, CompletedReportDto>
{
    public async Task<Result<CompletedReportDto>> Handle(GetCompletedReportQuery query, CancellationToken ct)
    {
        var allowed = await access.Check(query, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var periods = ReportPeriods.Of(query, query.Granularity);
        var completed = db.CompletedIn(periods, query).AsNoTracking();

        var moments = await completed.Select(task => task.CompletedAt!.Value).ToListAsync(ct);

        var byProject = await (
                from row in completed.GroupBy(task => task.ProjectId).Select(tasks => new { ProjectId = tasks.Key, Count = tasks.Count() })
                join project in db.Projects.IgnoreQueryFilters() on row.ProjectId equals project.Id
                orderby row.Count descending, project.Key
                select new ReportProjectCountDto(project.Id, project.Key, project.Name, row.Count))
            .ToListAsync(ct);

        var assignees = db.TaskAssignees.Where(assignee => completed.Any(task => task.Id == assignee.TaskId));
        if (query.UserId is { } userId)
        {
            assignees = assignees.Where(assignee => assignee.UserId == userId);
        }

        // PostgreSQL runs ToUpper as upper(), which takes no culture to give: the names in any case.
#pragma warning disable CA1304, CA1311
        var byUser = await (
                from row in assignees.GroupBy(assignee => assignee.UserId).Select(rows => new { UserId = rows.Key, Count = rows.Count() })
                join user in db.Users on row.UserId equals user.Id
                orderby row.Count descending, user.DisplayName.ToUpper(), user.Id
                select new ReportUserCountDto(user.Id, user.DisplayName, row.Count))
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311

        var counts = periods.Count(moments);
        return new CompletedReportDto(
            moments.Count,
            byUser,
            byProject,
            [.. periods.Starts.Select((start, index) => new ReportPeriodCountDto(start, counts[index]))]);
    }
}
