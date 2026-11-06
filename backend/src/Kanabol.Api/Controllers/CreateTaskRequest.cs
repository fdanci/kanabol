using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Api.Controllers;

/// <summary>Without columnId, the task starts in the board's first column. Priority goes over the wire as its name.</summary>
public sealed record CreateTaskRequest(
    string Title,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    Guid? ColumnId,
    Guid? AssigneeId,
    DateTimeOffset? DueDate);
