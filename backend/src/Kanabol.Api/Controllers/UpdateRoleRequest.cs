namespace Kanabol.Api.Controllers;

public sealed record UpdateRoleRequest(string Name, IReadOnlyList<string> Permissions);
