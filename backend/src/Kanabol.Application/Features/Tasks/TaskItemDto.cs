using System.Text.Json.Serialization;
using Kanabol.Application.Features.Attachments;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks;

/// <summary>
/// A task as its details panel shows it (UI spec section 7.5). Key is {Project.Key}-{Number}, as
/// KAN-123. CanChange says whether the caller can change it: a real member of an open project, while
/// the task isn't archived. Seeing it through projects.view_all or projects.manage alone doesn't.
/// Assignees come by name, whatever the case, and an empty list means nobody's. Attachments come from
/// the first uploaded (SPEC section 4.4).
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
    int? EstimatedMinutes,
    IReadOnlyList<TaskPersonDto> Assignees,
    Guid ColumnId,
    string ColumnName,
    int Position,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    TaskPersonDto? CreatedBy,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<AttachmentDto> Attachments,
    bool CanChange);
