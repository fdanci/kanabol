using System.Globalization;
using Kanabol.Application.Abstractions;
using Kanabol.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Settings;

public static class WorkSchedules
{
    public const string TimeFormat = "HH:mm";

    /// <summary>The one row the migration makes, or the default without it, as in tests that empty every table.</summary>
    public static async Task<WorkSchedule> Current(this IAppDbContext db, bool tracked, CancellationToken ct) =>
        await (tracked ? db.WorkSchedules : db.WorkSchedules.AsNoTracking()).FirstOrDefaultAsync(ct) ?? WorkSchedule.Default();

    public static WorkScheduleDto ToDto(this WorkSchedule schedule, ScheduleOptions options) => new(
        schedule.Days,
        schedule.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        schedule.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        options.TimeZone);

    /// <summary>As the log writes it: "1,2,3,4,5 08:30-16:30".</summary>
    public static string Describe(this WorkSchedule schedule) =>
        $"{schedule.WorkDays} {schedule.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture)}-{schedule.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture)}";

    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out time) && text!.Length == TimeFormat.Length;
}
