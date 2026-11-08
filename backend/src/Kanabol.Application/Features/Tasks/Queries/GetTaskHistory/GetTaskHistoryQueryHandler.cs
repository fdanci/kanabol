using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

/// <summary>
/// For whoever sees the project, and an archived task only for tasks.archive, as its comments
/// (TaskVisibility). From the newest, and entries of one moment by their id, so no entry is on two pages.
/// The history isn't sent on the hub: the panel reads it again after its own saves (SPEC section 9).
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

        var page = await (
                from entry in db.TaskHistoryEntries
                join author in db.Users on entry.ChangedById equals author.Id
                where entry.TaskId == query.TaskId
                orderby entry.ChangedAt descending, entry.Id descending
                select new Row(entry.Id, entry.Field, entry.OldValue, entry.NewValue, entry.ChangedAt, new TaskPersonDto(author.Id, author.DisplayName)))
            .AsNoTracking()
            .ToPage(query.Page, query.PageSize, ct);

        // Users are never deleted, so everyone assigned has a name; a column deleted since has none.
        var userIds = IdsIn(page.Items, TaskHistoryFields.Assignees);
        var userNames = userIds.Count == 0
            ? []
            : await db.Users.Where(user => userIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName })
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);
        var columnIds = IdsIn(page.Items, TaskHistoryFields.ColumnId);
        var columnNames = columnIds.Count == 0
            ? []
            : await db.BoardColumns.Where(column => column.ProjectId == task.Value.ProjectId && columnIds.Contains(column.Id))
                .Select(column => new { column.Id, column.Name })
                .ToDictionaryAsync(column => column.Id, column => column.Name, ct);

        return new Page<TaskHistoryEntryDto>(
            [.. page.Items.Select(row => row.ToDto(row.Field switch
            {
                TaskHistoryFields.Assignees => userNames,
                TaskHistoryFields.ColumnId => columnNames,
                _ => null,
            }))],
            page.Number,
            page.PageSize,
            page.TotalCount);
    }

    private static List<Guid> IdsIn(IEnumerable<Row> rows, string field) =>
        [.. rows.Where(row => row.Field == field).SelectMany(row => row.Ids()).Distinct()];

    private sealed record Row(Guid Id, string Field, string? OldValue, string? NewValue, DateTimeOffset ChangedAt, TaskPersonDto ChangedBy)
    {
        /// <summary>The ids in both values; a text that isn't one is left without a name.</summary>
        public IEnumerable<Guid> Ids() =>
            new[] { OldValue, NewValue }
                .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(text => Guid.TryParse(text, out var id) ? id : (Guid?)null)
                .OfType<Guid>();

        /// <summary>Only Assignees and ColumnId hold ids: the other fields name nothing.</summary>
        public TaskHistoryEntryDto ToDto(Dictionary<Guid, string>? known) =>
            new(
                Id,
                Field,
                OldValue,
                NewValue,
                ChangedAt,
                ChangedBy,
                known is null ? new Dictionary<Guid, string?>() : Ids().Distinct().ToDictionary(id => id, id => known.GetValueOrDefault(id)));
    }
}
