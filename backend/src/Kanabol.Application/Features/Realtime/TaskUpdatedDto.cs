namespace Kanabol.Application.Features.Realtime;

/// <summary>
/// TaskUpdated: also which field changed, Title, Description, Priority, DueDate or Assignees, as the
/// history names it, or Attachments, when a file was attached or taken off (SPEC section 9).
/// </summary>
public sealed record TaskUpdatedDto(
    Guid TaskId,
    Guid ProjectId,
    Guid ByUserId,
    string? OriginConnectionId,
    string Field);
