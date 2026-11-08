using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

/// <summary>
/// For whoever sees the project, and an archived task only for tasks.archive, as its comments
/// (TaskVisibility). From the newest, and entries of one moment by their id, so no entry is on two pages.
/// A description's entry comes without its texts, which GetTaskHistoryEntryQuery gives on demand. The
/// history isn't sent on the hub: the panel reads it again after its own saves (SPEC section 9).
/// </summary>
public sealed class GetTaskHistoryQueryHandler(IAppDbContext db, TaskVisibility visibility)
    : IQueryHandler<GetTaskHistoryQuery, Page<TaskHistoryEntryDto>>
{
    public async Task<Result<Page<TaskHistoryEntryDto>>> Handle(GetTaskHistoryQuery query, CancellationToken ct)
    {
        var task = await visibility.Check(query.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var page = await TaskHistoryRows
            .Read(db, db.TaskHistoryEntries.Where(entry => entry.TaskId == query.TaskId), withDescriptionTexts: false)
            .ToPage(query.Page, query.PageSize, ct);

        return new Page<TaskHistoryEntryDto>(
            await TaskHistoryRows.Named(db, task.Value.ProjectId, page.Items, ct),
            page.Number,
            page.PageSize,
            page.TotalCount);
    }
}
