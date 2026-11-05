namespace Kanabol.Api.Controllers;

public sealed record CreateRoleRequest(string Name, IReadOnlyList<string> Permissions);
