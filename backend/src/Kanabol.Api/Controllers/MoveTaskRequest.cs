namespace Kanabol.Api.Controllers;

/// <summary>
/// Without a position, or past the end, the task goes last in the column. The estimate counts only when the task
/// enters În lucru (SPEC section 4.4).
/// </summary>
public sealed record MoveTaskRequest(Guid ColumnId, int? Position, int? EstimatedMinutes);
