using System.Globalization;
using Kanabol.Domain.Common;

namespace Kanabol.Domain.Settings;

/// <summary>
/// The work schedule (SPEC sections 4.4, "Setările generale", and 5.1): the days and hours the worked time is
/// counted in, one row for the whole application. The days are ISO numbers, 1 for Monday to 7 for Sunday, kept
/// as text in order, "1,2,3,4,5"; the hours are local to Schedule:TimeZone, which the configuration holds.
/// </summary>
public sealed class WorkSchedule : Entity
{
    public static readonly TimeOnly DefaultStart = new(8, 30);
    public static readonly TimeOnly DefaultEnd = new(16, 30);

    /// <summary>The one row's id, so a second can't be made: shaped as UUID v7, as every id is.</summary>
    public static readonly Guid TheId = Guid.Parse("00000000-0000-7000-8000-000000000001");

    private WorkSchedule()
    {
    }

    /// <summary>The days, from Monday, as ISO numbers.</summary>
    public string WorkDays { get; private set; } = null!;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    public Guid? UpdatedById { get; private set; }

    public IReadOnlyList<int> Days => [.. WorkDays.Split(',').Select(day => int.Parse(day, CultureInfo.InvariantCulture))];

    /// <summary>Monday to Friday, 08:30 to 16:30, as the migration puts it (SPEC section 4.4).</summary>
    public static WorkSchedule Default() => new()
    {
        Id = TheId,
        WorkDays = Format([1, 2, 3, 4, 5]),
        StartTime = DefaultStart,
        EndTime = DefaultEnd,
    };

    /// <summary>At least one day, each once, from 1 for Monday to 7 for Sunday.</summary>
    public static bool AreWorkDays(IReadOnlyCollection<int>? days) =>
        days is { Count: > 0 } && days.All(day => day is >= 1 and <= 7) && days.Distinct().Count() == days.Count;

    public void Change(IReadOnlyCollection<int> days, TimeOnly start, TimeOnly end, Guid changedById, DateTimeOffset now)
    {
        if (!AreWorkDays(days))
        {
            throw new ArgumentException("A schedule has at least one day, each once, from 1 to 7.", nameof(days));
        }

        if (start >= end)
        {
            throw new ArgumentException("A schedule starts before it ends.", nameof(start));
        }

        WorkDays = Format(days);
        StartTime = start;
        EndTime = end;
        UpdatedAt = now;
        UpdatedById = changedById;
    }

    /// <summary>Whether the day is in the schedule.</summary>
    public bool IsAWorkDay(DayOfWeek day) => Days.Contains(IsoDay(day));

    /// <summary>The day as the schedule numbers it: DayOfWeek counts Sunday as 0, ISO as 7.</summary>
    public static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    private static string Format(IEnumerable<int> days) => string.Join(',', days.Order());
}
