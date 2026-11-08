using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;
using Kanabol.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// How the history's list and one of its entries read the same way (SPEC section 4.4, "Istoricul unui
/// task"): from the newest, entries of one moment by their id, each with who made it and the names of the
/// ids in its values.
/// </summary>
internal static class TaskHistoryRows
{
    /// <summary>
    /// The entries given, with their authors. The list leaves out a description's texts, up to 40 kB each,
    /// which the database doesn't even send then: one entry gives them, when someone opens its diff.
    /// </summary>
    public static IQueryable<TaskHistoryRow> Read(IAppDbContext db, IQueryable<TaskHistoryEntry> entries, bool withDescriptionTexts) =>
        from entry in entries
        join author in db.Users on entry.ChangedById equals author.Id
        orderby entry.ChangedAt descending, entry.Id descending
        select new TaskHistoryRow(
            entry.Id,
            entry.Field,
            withDescriptionTexts || entry.Field != TaskHistoryFields.Description ? entry.OldValue : null,
            withDescriptionTexts || entry.Field != TaskHistoryFields.Description ? entry.NewValue : null,
            entry.ChangedAt,
            new TaskPersonDto(author.Id, author.DisplayName));

    /// <summary>
    /// Only Assignees and ColumnId hold ids: the other fields name nothing. Users are never deleted, so
    /// everyone assigned has a name, also someone who left the project; a column deleted since has none.
    /// </summary>
    public static async Task<List<TaskHistoryEntryDto>> Named(IAppDbContext db, Guid projectId, IReadOnlyList<TaskHistoryRow> rows, CancellationToken ct)
    {
        var userIds = IdsIn(rows, TaskHistoryFields.Assignees);
        var userNames = userIds.Count == 0
            ? []
            : await db.Users.Where(user => userIds.Contains(user.Id))
                .Select(user => new { user.Id, user.DisplayName })
                .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);
        var columnIds = IdsIn(rows, TaskHistoryFields.ColumnId);
        var columnNames = columnIds.Count == 0
            ? []
            : await db.BoardColumns.Where(column => column.ProjectId == projectId && columnIds.Contains(column.Id))
                .Select(column => new { column.Id, column.Name })
                .ToDictionaryAsync(column => column.Id, column => column.Name, ct);

        return [.. rows.Select(row => ToDto(row, row.Field switch
        {
            TaskHistoryFields.Assignees => userNames,
            TaskHistoryFields.ColumnId => columnNames,
            _ => null,
        }))];
    }

    private static List<Guid> IdsIn(IEnumerable<TaskHistoryRow> rows, string field) =>
        [.. rows.Where(row => row.Field == field).SelectMany(row => row.Ids()).Distinct()];

    private static TaskHistoryEntryDto ToDto(TaskHistoryRow row, Dictionary<Guid, string>? known) =>
        new(
            row.Id,
            row.Field,
            row.OldValue,
            row.NewValue,
            row.ChangedAt,
            row.ChangedBy,
            known is null ? new Dictionary<Guid, string?>() : row.Ids().Distinct().ToDictionary(id => id, id => known.GetValueOrDefault(id)));
}
