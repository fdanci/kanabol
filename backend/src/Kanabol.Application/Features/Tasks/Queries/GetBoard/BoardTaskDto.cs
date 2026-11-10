using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

/// <summary>
/// A card on the board (UI spec section 7.4), with its assignees by name and the counts its icons show, the estimate,
/// the worked time up to now, whether it's working, and the pause's reason (SPEC section 4.4).
/// </summary>
public sealed record BoardTaskDto(
    Guid Id,
    string Key,
    int Number,
    string Title,
    Guid ColumnId,
    int Position,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    IReadOnlyList<TaskPersonDto> Assignees,
    DateTimeOffset? CompletedAt,
    int CommentCount,
    int AttachmentCount,
    int? EstimatedMinutes,
    long WorkedSeconds,
    bool IsWorking,
    string? PauseReason);
