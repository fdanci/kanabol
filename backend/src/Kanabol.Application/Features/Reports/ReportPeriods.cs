namespace Kanabol.Application.Features.Reports;

/// <summary>
/// A report's interval cut into periods (SPEC section 5.1.1): they start at From and step 7 days or a
/// calendar month on the clock From came with, so they fall on the Monday at midnight or the first of the
/// month where the person is. Each month is counted from From itself, so 31 August gives 30 September, then
/// 31 October. The last period ends at To.
/// The API doesn't know the person's time zone, only the offsets From and To came with: across a change
/// of summer time, October in Bucharest goes from +03:00 to +02:00. The steps are taken on the clock and
/// compared with To's own clock, so October stays one period instead of ending with a stray hour, and every
/// start keeps From's offset: a start past the change is an hour off, which only moves what was finished in
/// that hour. From and To are also kept in UTC, the only kind Npgsql writes to a timestamptz.
/// </summary>
public sealed class ReportPeriods
{
    private readonly DateTimeOffset[] _startsUtc;

    private ReportPeriods(DateTimeOffset from, DateTimeOffset to, ReportGranularity granularity)
    {
        From = from.ToUniversalTime();
        To = to.ToUniversalTime();

        var starts = new List<DateTimeOffset>();
        for (var index = 0; ; index++)
        {
            var clock = granularity == ReportGranularity.Month ? from.DateTime.AddMonths(index) : from.DateTime.AddDays(7 * index);
            var start = new DateTimeOffset(clock, from.Offset);
            if (clock >= to.DateTime || start >= to)
            {
                break;
            }

            starts.Add(start);
        }

        Starts = starts;
        _startsUtc = [.. starts.Select(start => start.ToUniversalTime())];
    }

    /// <summary>In UTC.</summary>
    public DateTimeOffset From { get; }

    /// <summary>In UTC, and not in the interval.</summary>
    public DateTimeOffset To { get; }

    /// <summary>Where each period starts, in the offset From came with.</summary>
    public IReadOnlyList<DateTimeOffset> Starts { get; }

    /// <summary>For a filter its validator passed: From and To are there.</summary>
    public static ReportPeriods Of(IReportFilter filter, ReportGranularity granularity = ReportGranularity.Week) =>
        new(filter.From!.Value, filter.To!.Value, granularity);

    /// <summary>The period a moment of the interval falls in, or -1 outside it.</summary>
    public int IndexOf(DateTimeOffset moment)
    {
        if (moment < From || moment >= To)
        {
            return -1;
        }

        var index = Array.BinarySearch(_startsUtc, moment.ToUniversalTime());
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>How many of the moments fall in each period, those outside left out.</summary>
    public int[] Count(IEnumerable<DateTimeOffset> moments)
    {
        var counts = new int[Starts.Count];
        foreach (var moment in moments)
        {
            var index = IndexOf(moment);
            if (index >= 0)
            {
                counts[index]++;
            }
        }

        return counts;
    }
}
