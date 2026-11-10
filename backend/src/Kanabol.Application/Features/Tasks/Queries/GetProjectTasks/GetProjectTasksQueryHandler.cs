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
    IClock clock,
    WorkedTime workedTime) : IQueryHandler<GetProjectTasksQuery, Page<TaskSummaryDto>>
{
    public async Task<Result<Page<TaskSummaryDto>>> Handle(GetProjectTasksQuery query, CancellationToken ct)
    {
        var allowed = archivedTasks.Check(query.IncludeArchived);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var exists = await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == query.ProjectId, ct);
        if (!exists)
        {
            return ProjectErrors.NotFound;
        }

        var access = await projectAccess.EnsureMemberAsync(query.ProjectId, currentUser.Id, ct);
        if (access.IsFailure)
        {
            return access.Error;
        }

        // The validator already refused any other text, so this only falls back for code that skips it.
        var sort = TaskSort.TryParse(query.Sort, out var parsed) ? parsed : new TaskSort(TaskSort.Key, Descending: true);
        var page = await db.Tasks(query.IncludeArchived)
            .Where(task => task.ProjectId == query.ProjectId)
            .OfAssignee(query.AssigneeId)
            .Filtered(db, query.Priorities, query.Overdue, query.Search, clock.UtcNow)
            .Rows(db)
            .Sorted(sort)
            .ToPage(query.Page, query.PageSize, ct);

        var items = new List<TaskSummaryDto>(page.Items.Count);
        foreach (var row in page.Items)
        {
            items.Add(row.ToSummaryDto(await workedTime.SecondsOf(row.WorkedSeconds, row.WorkStartedAt, ct)));
        }

        return new Page<TaskSummaryDto>(items, page.Number, page.PageSize, page.TotalCount);
    }
}
