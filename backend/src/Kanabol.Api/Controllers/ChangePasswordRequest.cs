namespace Kanabol.Api.Controllers;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
