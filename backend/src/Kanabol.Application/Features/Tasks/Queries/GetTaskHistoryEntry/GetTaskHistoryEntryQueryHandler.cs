using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistoryEntry;

/// <summary>
/// Under the list's rules: whoever sees the project, and an archived task only for tasks.archive
/// (TaskVisibility), with the same names. An entry that isn't this task's, another task's among them, is
/// History.NotFound. The panel asks for it only when someone opens a description's diff, which the browser
/// computes: the API gives the texts as they were saved (SPEC section 4.4, "Textele descrierii").
/// </summary>
public sealed class GetTaskHistoryEntryQueryHandler(IAppDbContext db, TaskVisibility visibility)
    : IQueryHandler<GetTaskHistoryEntryQuery, TaskHistoryEntryDto>
{
    public async Task<Result<TaskHistoryEntryDto>> Handle(GetTaskHistoryEntryQuery query, CancellationToken ct)
    {
        var task = await visibility.Check(query.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var row = await TaskHistoryRows
            .Read(db, db.TaskHistoryEntries.Where(entry => entry.TaskId == query.TaskId && entry.Id == query.EntryId), withDescriptionTexts: true)
            .FirstOrDefaultAsync(ct);
        if (row is null)
        {
            return HistoryErrors.NotFound;
        }

        return (await TaskHistoryRows.Named(db, task.Value.ProjectId, [row], ct))[0];
    }
}
