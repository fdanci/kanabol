namespace Kanabol.Api.Controllers;

/// <summary>Without a position, or past the end, the task goes last in the column.</summary>
public sealed record MoveTaskRequest(Guid ColumnId, int? Position);
