using System.Linq.Expressions;
using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;

/// <summary>
/// For whoever can see the project, archived or not (SPEC section 4.4). Overdue means open past the
/// deadline, outside a done column, as the project list counts it.
/// </summary>
public sealed class GetProjectTasksQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IProjectAccess projectAccess,
    ArchivedTasksAccess archivedTasks,
    IClock clock) : IQueryHandler<GetProjectTasksQuery, Page<TaskSummaryDto>>
{
    public async Task<Result<Page<TaskSummaryDto>>> Handle(GetProjectTasksQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(query.IncludeArchived);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var projectKey = await db.Projects.IgnoreQueryFilters()
            .Where(project => project.Id == query.ProjectId)
            .Select(project => project.Key)
            .FirstOrDefaultAsync(ct);
        if (projectKey is null)
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        var page = await Sorted(Rows(Filtered(query)), query.Sort).ToPage(query.Page, query.PageSize, ct);

        return new Page<TaskSummaryDto>(
            [.. page.Items.Select(row => row.ToDto(projectKey))],
            page.Number,
            page.PageSize,
            page.TotalCount);
    }

    private static IQueryable<Row> Sorted(IQueryable<Row> rows, string sortText)
    {
        // The validator already refused any other text, so this only falls back for code that skips it.
        var sort = TaskSort.TryParse(sortText, out var parsed) ? parsed : new TaskSort(TaskSort.Key, Descending: true);

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
            _ => First(rows, row => row.Number, sort.Descending),
        };
#pragma warning restore CA1304, CA1311

        return Then(sorted, row => row.Number, sort.Descending);
    }

    private static IOrderedQueryable<Row> First<TKey>(IQueryable<Row> rows, Expression<Func<Row, TKey>> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    private static IOrderedQueryable<Row> Then<TKey>(IOrderedQueryable<Row> rows, Expression<Func<Row, TKey>> key, bool descending) =>
        descending ? rows.ThenByDescending(key) : rows.ThenBy(key);

    private IQueryable<TaskItem> Filtered(GetProjectTasksQuery query)
    {
        var tasks = db.Tasks(query.IncludeArchived).Where(task => task.ProjectId == query.ProjectId);

        if (query.AssigneeId is { } assigneeId)
        {
            tasks = tasks.Where(task => task.Assignees.Any(assignee => assignee.UserId == assigneeId));
        }

        if (query.Priorities.Count > 0)
        {
            var priorities = query.Priorities.ToList();
            tasks = tasks.Where(task => priorities.Contains(task.Priority));
        }

        if (query.Overdue)
        {
            var now = clock.UtcNow;
            tasks = tasks.Where(task => task.DueDate < now
                && !db.BoardColumns.Any(column => column.Id == task.ColumnId && column.IsDone));
        }

        var search = query.Search?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(search))
        {
            // PostgreSQL runs this as upper() and strpos(): no culture to give, and % or _ are only letters.
#pragma warning disable CA1304, CA1311, CA1862
            tasks = tasks.Where(task => task.Title.ToUpper().Contains(search));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return tasks;
    }

    private IQueryable<Row> Rows(IQueryable<TaskItem> tasks) =>
        from task in tasks
        join column in db.BoardColumns on task.ColumnId equals column.Id
        select new Row
        {
            Id = task.Id,
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

    private sealed class Row
    {
        public Guid Id { get; init; }

        public int Number { get; init; }

        public string Title { get; init; } = "";

        public Guid ColumnId { get; init; }

        public string ColumnName { get; init; } = "";

        public int ColumnPosition { get; init; }

        public bool ColumnIsDone { get; init; }

        public TaskPriority Priority { get; init; }

        public int PriorityRank { get; init; }

        public DateTimeOffset? DueDate { get; init; }

        public List<TaskPersonDto> Assignees { get; init; } = [];

        public string? FirstAssigneeName { get; init; }

        public DateTimeOffset? CompletedAt { get; init; }

        public DateTimeOffset? ArchivedAt { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }

        public TaskSummaryDto ToDto(string projectKey) => new(
            Id,
            TaskKey.Format(projectKey, Number),
            Number,
            Title,
            ColumnId,
            ColumnName,
            ColumnIsDone,
            Priority,
            DueDate,
            Assignees.FirstOrDefault(),
            CompletedAt,
            ArchivedAt,
            UpdatedAt);
    }
}
