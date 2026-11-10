using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetMyTasks;

/// <summary>
/// For anyone signed in, about their own only, without a permission; archived tasks only with
/// includeArchived and tasks.archive, as in the project's list. Ties go by key from the newest, whichever
/// the direction, as the default sort's do.
/// </summary>
public sealed class GetMyTasksQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ArchivedTasksAccess archivedTasks,
    IClock clock,
    WorkedTime workedTime) : IQueryHandler<GetMyTasksQuery, Page<MyTaskDto>>
{
    public async Task<Result<Page<MyTaskDto>>> Handle(GetMyTasksQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(query.IncludeArchived);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var tasks = db.MyTasks(currentUser.Id, query.IncludeArchived);
        if (query.Open)
        {
            tasks = tasks.Where(task => task.CompletedAt == null);
        }

        // The validator already refused any other text, so this only falls back for code that skips it.
        var sort = TaskSort.TryParse(query.Sort, out var parsed) ? parsed : new TaskSort(TaskSort.DueDate, Descending: false);
        var page = await tasks
            .Filtered(db, query.Priorities, query.Overdue, query.Search, clock.UtcNow)
            .Rows(db)
            .Sorted(sort, tiesNewestFirst: true)
            .ToPage(query.Page, query.PageSize, ct);

        var items = new List<MyTaskDto>(page.Items.Count);
        foreach (var row in page.Items)
        {
            items.Add(row.ToMyTaskDto(await workedTime.SecondsOf(row.WorkedSeconds, row.WorkStartedAt, ct)));
        }

        return new Page<MyTaskDto>(items, page.Number, page.PageSize, page.TotalCount);
    }
}
