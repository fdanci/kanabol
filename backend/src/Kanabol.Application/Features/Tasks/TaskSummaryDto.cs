using System.Text.Json.Serialization;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// A row of the project's List view (UI spec section 7.3). UpdatedAt is the last change, or the creation
/// for a task never changed. ColumnKind and CompletedAt let the deadline chip leave finished tasks alone.
/// </summary>
public sealed record TaskSummaryDto(
    Guid Id,
    string Key,
    int Number,
    string Title,
    Guid ColumnId,
    string ColumnName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ColumnKind>))] ColumnKind ColumnKind,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<TaskPersonDto> Assignees,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset UpdatedAt);
