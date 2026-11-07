using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Api.Controllers;

/// <summary>Without columnId, the task starts in the board's first column, and without assigneeIds it's nobody's. Priority goes over the wire as its name.</summary>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    Guid? ColumnId,
    IReadOnlyList<Guid>? AssigneeIds,
    DateTimeOffset? DueDate);
