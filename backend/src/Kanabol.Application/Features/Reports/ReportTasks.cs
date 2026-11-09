using Kanabol.Application.Abstractions;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Reports;

public static class ReportTasks
{
    /// <summary>
    /// The tasks completed in the interval (SPEC section 5.1.1): archived or not, since archiving after the
    /// work doesn't undo it, and one archived before it was finished has no completed_at; in projects
    /// archived or not, unless a project is asked for; only those the person is assigned to, with one.
    /// </summary>
    public static IQueryable<TaskItem> CompletedIn(this IAppDbContext db, ReportPeriods periods, IReportFilter filter)
    {
        var from = periods.From;
        var to = periods.To;
        var completed = db.TaskItems.IgnoreQueryFilters().Where(task => task.CompletedAt >= from && task.CompletedAt < to);
        return db.Narrowed(completed, filter);
    }

    /// <summary>The project's tasks only, and only the person's, when the filter names them.</summary>
    public static IQueryable<TaskItem> Narrowed(this IAppDbContext db, IQueryable<TaskItem> tasks, IReportFilter filter)
    {
        if (filter.ProjectId is { } projectId)
        {
            tasks = tasks.Where(task => task.ProjectId == projectId);
        }

        if (filter.UserId is { } userId)
        {
            tasks = tasks.Where(task => db.TaskAssignees.Any(assignee => assignee.TaskId == task.Id && assignee.UserId == userId));
        }

        return tasks;
    }
}
