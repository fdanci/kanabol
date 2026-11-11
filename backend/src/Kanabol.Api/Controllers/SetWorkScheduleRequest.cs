namespace Kanabol.Api.Controllers;

/// <summary>The working days as ISO numbers, 1 for Monday to 7 for Sunday, and the hours as HH:mm.</summary>
public sealed record SetWorkScheduleRequest(IReadOnlyList<int>? Days, string? Start, string? End);
