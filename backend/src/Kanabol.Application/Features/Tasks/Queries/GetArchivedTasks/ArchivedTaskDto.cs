using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetArchivedTasks;

/// <summary>
/// A card of the column "Arhivate" (SPEC section 4.4, UI spec section 7.4): the board's card, with when and by
/// whom it was archived, and the column it comes back to; ArchivedBy is null only for a row that names nobody.
/// An archived task doesn't work, so its worked seconds are the stored ones and IsWorking is false.
/// </summary>
public sealed record ArchivedTaskDto(
    Guid Id,
    string Key,
    int Number,
    string Title,
    Guid ColumnId,
    string ColumnName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<TaskPersonDto> Assignees,
    DateTimeOffset? CompletedAt,
    int CommentCount,
    int AttachmentCount,
    int? EstimatedMinutes,
    long WorkedSeconds,
    bool IsWorking,
    string? PauseReason,
    DateTimeOffset ArchivedAt,
    TaskPersonDto? ArchivedBy);
