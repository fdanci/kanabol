namespace Kanabol.Api.Controllers;

public sealed record CreateUserRequest(string Email, string DisplayName, Guid RoleId, string InitialPassword);
