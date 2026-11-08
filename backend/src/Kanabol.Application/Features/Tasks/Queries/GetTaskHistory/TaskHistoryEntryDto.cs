namespace Kanabol.Application.Features.Tasks.Queries.GetTaskHistory;

/// <summary>
/// One change of a task, as its timeline shows it (UI spec section 7.5). The values are the entry's text
/// (SPEC section 5.1): the ids of Assignees, sorted and separated by commas, and of ColumnId. Names gives
/// each of those ids its name: a user's, also for someone who left the project or was deactivated, and a
/// column's, null for a column deleted since, which the timeline calls "coloană ștearsă". In the list a
/// Description entry has both values null; GET /api/tasks/{id}/history/{entryId} gives its texts.
/// </summary>
public sealed record TaskHistoryEntryDto(
    Guid Id,
    string Field,
    string? OldValue,
    string? NewValue,
    DateTimeOffset ChangedAt,
    TaskPersonDto ChangedBy,
    IReadOnlyDictionary<Guid, string?> Names);
