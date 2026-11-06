namespace Kanabol.Api.Controllers;

/// <summary>Null or blank clears it.</summary>
public sealed record ChangeTaskDescriptionRequest(string? Description);
