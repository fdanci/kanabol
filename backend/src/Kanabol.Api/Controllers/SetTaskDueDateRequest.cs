namespace Kanabol.Api.Controllers;

/// <summary>Null clears the deadline.</summary>
public sealed record SetTaskDueDateRequest(DateTimeOffset? DueDate);
