namespace Kanabol.Api.Controllers;

public sealed record CreateProjectRequest(string Name, string Key, string? Description);
