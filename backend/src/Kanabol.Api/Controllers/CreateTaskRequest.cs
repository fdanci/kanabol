using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Api.Controllers;

/// <summary>
/// The task starts in Backlog: a columnId, when given, has to be it. Without assigneeIds it's nobody's, and the
/// estimate is optional. Priority goes over the wire as its name.
/// </summary>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    Guid? ColumnId,
    IReadOnlyList<Guid>? AssigneeIds,
    DateTimeOffset? DueDate,
    int? EstimatedMinutes);
