namespace Kanabol.Api.Controllers;

/// <summary>
/// Without a position, or past the end, the task goes last in the column. The estimate counts only when the task
/// enters În lucru, and the pause's reason when it enters În pauză (SPEC section 4.4).
/// </summary>
public sealed record MoveTaskRequest(Guid ColumnId, int? Position, int? EstimatedMinutes, string? PauseReason);
