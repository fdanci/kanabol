namespace Kanabol.Domain.Settings;

/// <summary>The work schedule with the time zone its hours are in: what the worked time is counted against.</summary>
public sealed record WorkingHours(WorkSchedule Schedule, TimeZoneInfo Zone)
{
    public long Between(DateTimeOffset from, DateTimeOffset to) => WorkingTime.Between(from, to, Schedule, Zone);
}
