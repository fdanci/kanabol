namespace Kanabol.Domain.Settings;

/// <summary>
/// The worked time (SPEC section 4.4, "Timpul lucrat"): what part of an interval falls in the work schedule, its
/// days and hours read in the schedule's time zone, so a day keeps its hours across a change of summer time.
/// </summary>
public static class WorkingTime
{
    /// <summary>The whole seconds of [from, to) inside the schedule; nothing for an empty or reversed interval.</summary>
    public static long Between(DateTimeOffset from, DateTimeOffset to, WorkSchedule schedule, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(zone);
        if (to <= from)
        {
            return 0;
        }

        var start = from.UtcDateTime;
        var end = to.UtcDateTime;
        var total = TimeSpan.Zero;
        // Read once: Days parses the row's text each time.
        var days = schedule.Days;
        var last = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(end, zone));
        for (var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(start, zone)); day <= last; day = day.AddDays(1))
        {
            if (!days.Contains(WorkSchedule.IsoDay(day.DayOfWeek)))
            {
                continue;
            }

            var opens = ToUtc(day.ToDateTime(schedule.StartTime), zone, first: true);
            var closes = ToUtc(day.ToDateTime(schedule.EndTime), zone, first: false);
            var overlapStart = opens > start ? opens : start;
            var overlapEnd = closes < end ? closes : end;
            if (overlapEnd > overlapStart)
            {
                total += overlapEnd - overlapStart;
            }
        }

        return (long)Math.Floor(total.TotalSeconds);
    }

    /// <summary>
    /// A local moment as an instant. One the change to summer time skips is the moment the clock jumps to; one
    /// that comes twice, when winter time comes back, is the first time it comes for an opening hour and the
    /// second for a closing one, so the day keeps the hour it gains.
    /// </summary>
    private static DateTime ToUtc(DateTime local, TimeZoneInfo zone, bool first)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        for (var minutes = 0; zone.IsInvalidTime(local) && minutes < 24 * 60; minutes++)
        {
            local = local.AddMinutes(1);
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var offset = first ? offsets.Max() : offsets.Min();
            return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
