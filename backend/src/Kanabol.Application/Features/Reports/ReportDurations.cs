namespace Kanabol.Application.Features.Reports;

/// <summary>
/// An average and a median of durations, in memory (SPEC section 5.1.1): EF doesn't give a median, and
/// the interval's 366 days bound how many there are. The median of an even count is the mean of the two in
/// the middle; both round to the nearest second, half away from zero.
/// </summary>
public static class ReportDurations
{
    public static ReportDurationDto Of(IReadOnlyCollection<TimeSpan> durations)
    {
        if (durations.Count == 0)
        {
            return new ReportDurationDto(null, null);
        }

        var seconds = durations.Select(duration => duration.TotalSeconds).Order().ToArray();
        var middle = seconds.Length / 2;
        var median = seconds.Length % 2 == 1 ? seconds[middle] : (seconds[middle - 1] + seconds[middle]) / 2;
        return new ReportDurationDto(Whole(seconds.Average()), Whole(median));
    }

    private static long Whole(double seconds) => (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
}
