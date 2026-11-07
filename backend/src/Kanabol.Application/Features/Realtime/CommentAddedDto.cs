namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// CommentAdded: which comment, on which task, who, and the connection of the tab that wrote it. The board
/// reads itself again, for the count on the card; an open panel doesn't listen in v1 (SPEC section 9).
/// </summary>
public sealed record CommentAddedDto(
    Guid TaskId,
    Guid ProjectId,
    Guid ByUserId,
    string? OriginConnectionId,
    Guid CommentId);
