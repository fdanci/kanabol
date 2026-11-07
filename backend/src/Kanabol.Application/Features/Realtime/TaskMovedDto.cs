namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// TaskMoved: also where the task went, so a board moves the card itself, with its animation, without
/// reading the board again (SPEC section 9, UI spec section 7.4). The tasks around it moved quietly.
/// </summary>
public sealed record TaskMovedDto(
    Guid TaskId,
    Guid ProjectId,
    Guid ByUserId,
    string? OriginConnectionId,
    Guid ColumnId,
    int Position);
