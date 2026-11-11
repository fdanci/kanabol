using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Reports.Queries.GetWorkTimeReport;

/// <summary>
/// The estimate against the worked time of the tasks finished in the interval (SPEC section 5.1.1, group 22),
/// the tasks the completed report counts. Only those with an estimate and some time worked go into the sums,
/// the deviation and the overruns; the counts take every task. The worked time is the schedule's (SPEC section
/// 4.4), stored when the task left În lucru, which a finished task did. Summed in the database, by person and
/// by project, a task with several assignees whole for each of them.
/// </summary>
public sealed class GetWorkTimeReportQueryHandler(IAppDbContext db, ReportAccess access)
    : IQueryHandler<GetWorkTimeReportQuery, WorkTimeReportDto>
{
    public const int MaxOverruns = 20;

    public async Task<Result<WorkTimeReportDto>> Handle(GetWorkTimeReportQuery query, CancellationToken ct)
    {
        var allowed = await access.Check(query, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var completed = db.CompletedIn(ReportPeriods.Of(query), query).AsNoTracking();
        var tasks = await completed
            .Select(task => new { Estimated = task.EstimatedMinutes * 60L, Worked = (long)task.WorkedSeconds })
            .ToListAsync(ct);
        var counted = tasks.Where(task => task.Estimated is not null && task.Worked > 0).ToList();
        long estimated = counted.Sum(task => task.Estimated!.Value);
        long worked = counted.Sum(task => task.Worked);

        var byProject = await (
                from row in completed
                    .GroupBy(task => task.ProjectId)
                    .Select(rows => new
                    {
                        ProjectId = rows.Key,
                        Count = rows.Count(),
                        Estimated = rows.Sum(task => task.EstimatedMinutes != null && task.WorkedSeconds > 0 ? task.EstimatedMinutes.Value * 60L : 0L),
                        Worked = rows.Sum(task => task.EstimatedMinutes != null && task.WorkedSeconds > 0 ? (long)task.WorkedSeconds : 0L),
                    })
                join project in db.Projects.IgnoreQueryFilters() on row.ProjectId equals project.Id
                orderby row.Count descending, project.Key
                select new ReportProjectWorkTimeDto(project.Id, project.Key, project.Name, row.Count, row.Estimated, row.Worked))
            .ToListAsync(ct);

        var assigned =
            from assignee in db.TaskAssignees
            join task in completed on assignee.TaskId equals task.Id
            select new { assignee.UserId, task.EstimatedMinutes, task.WorkedSeconds };
        if (query.UserId is { } userId)
        {
            assigned = assigned.Where(row => row.UserId == userId);
        }

        // PostgreSQL runs ToUpper as upper(), which takes no culture to give: the names in any case.
#pragma warning disable CA1304, CA1311
        var byUser = await (
                from row in assigned
                    .GroupBy(row => row.UserId)
                    .Select(rows => new
                    {
                        UserId = rows.Key,
                        Count = rows.Count(),
                        Estimated = rows.Sum(task => task.EstimatedMinutes != null && task.WorkedSeconds > 0 ? task.EstimatedMinutes.Value * 60L : 0L),
                        Worked = rows.Sum(task => task.EstimatedMinutes != null && task.WorkedSeconds > 0 ? (long)task.WorkedSeconds : 0L),
                    })
                join user in db.Users on row.UserId equals user.Id
                orderby row.Count descending, user.DisplayName.ToUpper(), user.Id
                select new ReportUserWorkTimeDto(user.Id, user.DisplayName, row.Count, row.Estimated, row.Worked))
            .ToListAsync(ct);
#pragma warning restore CA1304, CA1311

        var over = await (
                from task in completed
                where task.EstimatedMinutes != null && task.WorkedSeconds > task.EstimatedMinutes * 60
                join project in db.Projects.IgnoreQueryFilters() on task.ProjectId equals project.Id
                orderby task.WorkedSeconds - (task.EstimatedMinutes!.Value * 60) descending, project.Key, task.Number
                select new { task.Id, project.Key, task.Number, task.Title, Estimated = task.EstimatedMinutes!.Value * 60L, Worked = (long)task.WorkedSeconds })
            .Take(MaxOverruns)
            .ToListAsync(ct);

        return new WorkTimeReportDto(
            tasks.Count,
            counted.Count,
            estimated,
            worked,
            estimated > 0 ? (worked - estimated) / (double)estimated : null,
            counted.Count(task => task.Worked > task.Estimated),
            byUser,
            byProject,
            [.. over.Select(task => new ReportOverrunDto(task.Id, TaskKey.Format(task.Key, task.Number), task.Title, task.Estimated, task.Worked))]);
    }
}
