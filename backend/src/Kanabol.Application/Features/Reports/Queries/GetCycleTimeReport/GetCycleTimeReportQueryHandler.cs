using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Reports.Queries.GetCycleTimeReport;

/// <summary>
/// Lead and cycle time of the tasks finished in the interval, and with a project the time in each of its
/// columns (SPEC section 5.1.1). EF gives no median: the handler reads each task's creation, completion and
/// ColumnId entries, and works out the averages and medians in memory, which the 366 days bound.
/// </summary>
public sealed class GetCycleTimeReportQueryHandler(IAppDbContext db, ReportAccess access)
    : IQueryHandler<GetCycleTimeReportQuery, CycleTimeReportDto>
{
    public async Task<Result<CycleTimeReportDto>> Handle(GetCycleTimeReportQuery query, CancellationToken ct)
    {
        var allowed = await access.Check(query, ct);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var periods = ReportPeriods.Of(query);
        var timelines = await db.CompletedIn(periods, query)
            .AsNoTracking()
            .Select(task => new FinishedTaskTimeline(
                task.ProjectId,
                task.ColumnId,
                task.CreatedAt,
                task.CompletedAt!.Value,
                db.TaskHistoryEntries
                    .Where(entry => entry.TaskId == task.Id && entry.Field == TaskHistoryFields.ColumnId)
                    .OrderBy(entry => entry.ChangedAt)
                    .ThenBy(entry => entry.Id)
                    .Select(entry => new ColumnMove(entry.OldValue, entry.NewValue, entry.ChangedAt))
                    .ToList()))
            .ToListAsync(ct);

        // The column at position 0 of each project, as the board shows it now.
        var projectIds = timelines.Select(timeline => timeline.ProjectId).Distinct().ToList();
        var firstColumns = await db.BoardColumns
            .Where(column => projectIds.Contains(column.ProjectId) && column.Position == 0)
            .ToDictionaryAsync(column => column.ProjectId, column => column.Id, ct);

        return new CycleTimeReportDto(
            timelines.Count,
            ReportDurations.Of([.. timelines.Select(timeline => timeline.LeadTime)]),
            ReportDurations.Of([.. timelines.Select(timeline => timeline.CycleTime(firstColumns.GetValueOrDefault(timeline.ProjectId)))]),
            query.ProjectId is { } projectId ? await PerColumn(projectId, timelines, ct) : null);
    }

    /// <summary>The project's columns from the left, each with the stays of the tasks that were in it; a column deleted since isn't there.</summary>
    private async Task<List<ReportColumnDurationDto>> PerColumn(Guid projectId, List<FinishedTaskTimeline> timelines, CancellationToken ct)
    {
        var columns = await db.BoardColumns
            .Where(column => column.ProjectId == projectId)
            .OrderBy(column => column.Position)
            .Select(column => new { column.Id, column.Name })
            .ToListAsync(ct);
        var stays = timelines.Select(timeline => timeline.Stays()).ToList();

        return
        [
            .. columns.Select(column =>
            {
                var durations = ReportDurations.Of([.. stays.Where(task => task.ContainsKey(column.Id)).Select(task => task[column.Id])]);
                return new ReportColumnDurationDto(column.Id, column.Name, durations.AverageSeconds, durations.MedianSeconds);
            }),
        ];
    }
}
