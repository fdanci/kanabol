using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// A row of "Task-urile mele" and of the main panel's table (UI spec section 7.2): a row of the project's
/// list, with the project it is in.
/// </summary>
public sealed record MyTaskDto(
    Guid Id,
    Guid ProjectId,
    string ProjectKey,
    string ProjectName,
    string Key,
    int Number,
    string Title,
    Guid ColumnId,
    string ColumnName,
    bool ColumnIsDone,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<TaskPersonDto> Assignees,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset UpdatedAt);
