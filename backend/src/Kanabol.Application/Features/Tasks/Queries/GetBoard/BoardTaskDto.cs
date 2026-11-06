using System.Text.Json.Serialization;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

/// <summary>A card on the board (UI spec section 7.4), with the counts its icons show.</summary>
public sealed record BoardTaskDto(
    Guid Id,
    string Key,
    int Number,
    string Title,
    Guid ColumnId,
    int Position,
    [property: JsonConverter(typeof(JsonStringEnumConverter<TaskPriority>))] TaskPriority Priority,
    DateTimeOffset? DueDate,
    TaskPersonDto? Assignee,
    DateTimeOffset? CompletedAt,
    int CommentCount,
    int AttachmentCount);
