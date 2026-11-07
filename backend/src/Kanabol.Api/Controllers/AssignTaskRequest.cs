namespace Kanabol.Api.Controllers;

/// <summary>The whole list of assignees, which takes the place of the one before: an empty one leaves the task to nobody.</summary>
public sealed record AssignTaskRequest(IReadOnlyList<Guid> AssigneeIds);
