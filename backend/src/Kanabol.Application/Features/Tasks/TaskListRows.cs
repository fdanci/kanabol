using System.Linq.Expressions;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// What the project's list and the caller's own tasks share (SPEC section 4.4): the filters, the rows and
/// the sorts. Overdue means open past the deadline, outside a done column, as the project list counts it.
/// The key is the project's key, then the number, which in one project is the number alone.
/// </summary>
internal static class TaskListRows
{
    /// <summary>The tasks that have them among their assignees, or every one without an id.</summary>
    public static IQueryable<TaskItem> OfAssignee(this IQueryable<TaskItem> tasks, Guid? assigneeId) =>
        assigneeId is { } userId ? tasks.Where(task => task.Assignees.Any(assignee => assignee.UserId == userId)) : tasks;

    public static IQueryable<TaskItem> Filtered(
        this IQueryable<TaskItem> tasks,
        IAppDbContext db,
        IReadOnlyList<TaskPriority> priorities,
        bool overdue,
        string? search,
        DateTimeOffset now)
    {
        if (priorities.Count > 0)
        {
            var wanted = priorities.ToList();
            tasks = tasks.Where(task => wanted.Contains(task.Priority));
        }

        if (overdue)
        {
            tasks = tasks.Where(task => task.DueDate < now
                && !db.BoardColumns.Any(column => column.Id == task.ColumnId && column.IsDone));
        }

        var text = search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(text))
        {
            // PostgreSQL runs this as upper() and strpos(): no culture to give, and % or _ are only letters.
#pragma warning disable CA1304, CA1311, CA1862
            tasks = tasks.Where(task => task.Title.ToUpper().Contains(text));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return tasks;
    }

    /// <summary>
    /// Sorted by the field asked for. Ties go by key in the same direction, so the pages never overlap, or
    /// from the newest whichever the direction when tiesNewestFirst says so.
    /// </summary>
    public static IQueryable<TaskListRow> Sorted(this IQueryable<TaskListRow> rows, TaskSort sort, bool tiesNewestFirst = false)
    {
        // PostgreSQL runs ToUpper as upper(), which takes no culture to give.
#pragma warning disable CA1304, CA1311
        var sorted = sort.Field switch
        {
            TaskSort.Title => First(rows, row => row.Title.ToUpper(), sort.Descending),
            TaskSort.Column => First(rows, row => row.ColumnPosition, sort.Descending),
            TaskSort.Priority => First(rows, row => row.PriorityRank, sort.Descending),

            // Without a deadline or an assignee last, whichever the direction.
            TaskSort.DueDate => Then(rows.OrderBy(row => row.DueDate == null), row => row.DueDate, sort.Descending),
            TaskSort.Assignee => Then(rows.OrderBy(row => row.FirstAssigneeName == null), row => row.FirstAssigneeName, sort.Descending),
            TaskSort.Updated => First(rows, row => row.UpdatedAt, sort.Descending),
            _ => First(rows, row => row.ProjectKey, sort.Descending),
        };
#pragma warning restore CA1304, CA1311

        if (sort.Field == TaskSort.Key)
        {
            return Then(sorted, row => row.Number, sort.Descending);
        }

        var tiesDescending = tiesNewestFirst || sort.Descending;
        return Then(Then(sorted, row => row.ProjectKey, tiesDescending), row => row.Number, tiesDescending);
    }

    public static IQueryable<TaskListRow> Rows(this IQueryable<TaskItem> tasks, IAppDbContext db) =>
        from task in tasks
        join column in db.BoardColumns on task.ColumnId equals column.Id
        join project in db.Projects.IgnoreQueryFilters() on task.ProjectId equals project.Id
        select new TaskListRow
        {
            Id = task.Id,
            ProjectId = project.Id,
            ProjectKey = project.Key,
            ProjectName = project.Name,
            Number = task.Number,
            Title = task.Title,
            ColumnId = column.Id,
            ColumnName = column.Name,
            ColumnPosition = column.Position,
            ColumnIsDone = column.IsDone,
            Priority = task.Priority,
            PriorityRank = task.Priority == TaskPriority.Critical ? 3 : task.Priority == TaskPriority.High ? 2 : task.Priority == TaskPriority.Medium ? 1 : 0,
            DueDate = task.DueDate,
            // PostgreSQL runs ToUpper as upper(), which takes no culture to give: the names in any case.
#pragma warning disable CA1304, CA1311
            Assignees = (
                    from assignee in db.TaskAssignees
                    join user in db.Users on assignee.UserId equals user.Id
                    where assignee.TaskId == task.Id
                    orderby user.DisplayName.ToUpper(), user.Id
                    select new TaskPersonDto(user.Id, user.DisplayName))
                .ToList(),

            // The first name in alphabetical order, which the sort by assignee goes by.
            FirstAssigneeName = (
                    from assignee in db.TaskAssignees
                    join user in db.Users on assignee.UserId equals user.Id
                    where assignee.TaskId == task.Id
                    select user.DisplayName.ToUpper())
                .OrderBy(name => name)
                .FirstOrDefault(),
#pragma warning restore CA1304, CA1311
            CompletedAt = task.CompletedAt,
            ArchivedAt = task.ArchivedAt,
            UpdatedAt = task.UpdatedAt ?? task.CreatedAt,
        };

    private static IOrderedQueryable<TaskListRow> First<TKey>(IQueryable<TaskListRow> rows, Expression<Func<TaskListRow, TKey>> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    private static IOrderedQueryable<TaskListRow> Then<TKey>(IOrderedQueryable<TaskListRow> rows, Expression<Func<TaskListRow, TKey>> key, bool descending) =>
        descending ? rows.ThenByDescending(key) : rows.ThenBy(key);
}
