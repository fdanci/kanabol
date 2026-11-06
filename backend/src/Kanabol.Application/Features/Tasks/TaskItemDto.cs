using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// A task as its details panel shows it (UI spec section 7.5). Key is {Project.Key}-{Number}, as
/// KAN-123. CanChange says whether the caller can change it: a real member of an open project, while
/// the task isn't archived. Seeing it through projects.view_all or projects.manage alone doesn't.
/// </summary>
public sealed record TaskItemDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string Key,
    int Number,
    string Title,
    string? Description,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    TaskPersonDto? Assignee,
    Guid ColumnId,
    string ColumnName,
    int Position,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    TaskPersonDto? CreatedBy,
    DateTimeOffset? UpdatedAt,
    bool CanChange);
