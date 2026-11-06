namespace Kanabol.Api.Controllers;

/// <summary>No key: it never changes after creation, so one sent here is ignored.</summary>
public sealed record UpdateProjectRequest(string Name, string? Description);
