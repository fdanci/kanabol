namespace Kanabol.Api.Controllers;

/// <summary>Null leaves the task to nobody.</summary>
public sealed record AssignTaskRequest(Guid? AssigneeId);
