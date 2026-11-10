namespace Kanabol.Application.Features.Settings;

/// <summary>The working days as ISO numbers, 1 for Monday, the hours as HH:mm, and the configuration's time zone.</summary>
public sealed record WorkScheduleDto(IReadOnlyList<int> Days, string Start, string End, string TimeZone);
