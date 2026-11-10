using Kanabol.Domain.Settings;

namespace Kanabol.Domain.Tests.Settings;

/// <summary>
/// WorkingTime.Between (SPEC section 4.4, "Timpul lucrat", group 22): the seconds of an interval that fall in the
/// schedule's days and hours, in its time zone. Bucharest is UTC+2 in winter and UTC+3 in summer; 2026 goes to
/// summer time on Sunday 29 March at 03:00 and back on Sunday 25 October at 04:00.
/// </summary>
public sealed class WorkingTimeTests
{
    private static readonly TimeZoneInfo _bucharest = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");
    private static readonly TimeSpan _winter = TimeSpan.FromHours(2);
    private static readonly TimeSpan _summer = TimeSpan.FromHours(3);

    private static readonly WorkSchedule _weekdays = WorkSchedule.Default();

    private static long Between(DateTimeOffset from, DateTimeOffset to, WorkSchedule? schedule = null) =>
        WorkingTime.Between(from, to, schedule ?? _weekdays, _bucharest);

    // Wednesday 11 November 2026, in Bucharest's winter time.
    private static DateTimeOffset Wednesday(int hour, int minute = 0, int second = 0) => new(2026, 11, 11, hour, minute, second, _winter);

    private static WorkSchedule Schedule(int[] days, TimeOnly start, TimeOnly end)
    {
        var schedule = WorkSchedule.Default();
        schedule.Change(days, start, end, Guid.CreateVersion7(), DateTimeOffset.UnixEpoch);
        return schedule;
    }

    [Fact]
    public void Between_TheSameDayInsideTheHours_IsTheWholeInterval() =>
        Between(Wednesday(9, 15), Wednesday(11, 45, 30)).Should().Be((2 * 3600) + (30 * 60) + 30);

    [Fact]
    public void Between_AStartBeforeTheHours_CountsFromTheirStart() =>
        Between(Wednesday(6, 0), Wednesday(10, 0)).Should().Be(90 * 60);

    [Fact]
    public void Between_AnEndAfterTheHours_CountsToTheirEnd() =>
        Between(Wednesday(15, 0), Wednesday(20, 0)).Should().Be(90 * 60);

    [Fact]
    public void Between_AWholeDayAroundTheHours_IsTheEightHours() =>
        Between(Wednesday(0, 0), Wednesday(23, 59)).Should().Be(8 * 3600);

    [Fact]
    public void Between_OverNight_CountsTheEveningAndTheMorningOnly() =>
        Between(Wednesday(15, 30), new DateTimeOffset(2026, 11, 12, 9, 30, 0, _winter)).Should().Be(2 * 3600);

    // Friday 13 November 15:30 to Monday 16 November 09:30: Saturday and Sunday count nothing.
    [Fact]
    public void Between_OverTheWeekend_SkipsSaturdayAndSunday() =>
        Between(new DateTimeOffset(2026, 11, 13, 15, 30, 0, _winter), new DateTimeOffset(2026, 11, 16, 9, 30, 0, _winter)).Should().Be(2 * 3600);

    [Fact]
    public void Between_AWeek_IsFiveDaysOfEightHours() =>
        Between(new DateTimeOffset(2026, 11, 9, 0, 0, 0, _winter), new DateTimeOffset(2026, 11, 16, 0, 0, 0, _winter)).Should().Be(5 * 8 * 3600);

    [Fact]
    public void Between_InstantsInAnyOffset_AreTheSameMoments() =>
        Between(Wednesday(9, 0).ToUniversalTime(), Wednesday(10, 0).ToOffset(TimeSpan.FromHours(-5))).Should().Be(3600);

    [Fact]
    public void Between_AnEmptyInterval_IsNothing() => Between(Wednesday(10, 0), Wednesday(10, 0)).Should().Be(0);

    [Fact]
    public void Between_AReversedInterval_IsNothing() => Between(Wednesday(12, 0), Wednesday(10, 0)).Should().Be(0);

    [Fact]
    public void Between_OutsideTheHours_IsNothing() => Between(Wednesday(17, 0), Wednesday(23, 0)).Should().Be(0);

    // The hours are Bucharest's: 08:30 there is 06:30 UTC in winter and 05:30 in summer.
    [Fact]
    public void Between_InSummer_TakesTheHoursInSummerTime()
    {
        var day = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero);

        Between(day.AddHours(5).AddMinutes(30), day.AddHours(6).AddMinutes(30)).Should().Be(3600);
        Between(day.AddHours(13), day.AddHours(14)).Should().Be(30 * 60, "16:30 in Bucharest is 13:30 UTC in summer");
    }

    // Sunday 29 March 2026: 03:00 becomes 04:00, so 00:00 to 08:00 there is seven hours long.
    [Fact]
    public void Between_OnTheDaySummerTimeStarts_CountsTheShorterDay()
    {
        var sundays = Schedule([7], new TimeOnly(0, 0), new TimeOnly(8, 0));

        Between(new DateTimeOffset(2026, 3, 28, 12, 0, 0, _winter), new DateTimeOffset(2026, 3, 29, 12, 0, 0, _summer), sundays).Should().Be(7 * 3600);
        Between(new DateTimeOffset(2026, 3, 29, 2, 30, 0, _winter), new DateTimeOffset(2026, 3, 29, 4, 30, 0, _summer), sundays).Should().Be(3600);
    }

    // Sunday 25 October 2026: 04:00 becomes 03:00, so 00:00 to 08:00 there is nine hours long.
    [Fact]
    public void Between_OnTheDayWinterTimeComesBack_CountsTheLongerDay()
    {
        var sundays = Schedule([7], new TimeOnly(0, 0), new TimeOnly(8, 0));

        Between(new DateTimeOffset(2026, 10, 24, 12, 0, 0, _summer), new DateTimeOffset(2026, 10, 25, 12, 0, 0, _winter), sundays).Should().Be(9 * 3600);
    }

    // The weekdays' hours, across the change of time: every day keeps its eight hours.
    [Fact]
    public void Between_AWeekAcrossAChangeOfTime_IsStillFiveDaysOfEightHours() =>
        Between(new DateTimeOffset(2026, 10, 19, 0, 0, 0, _summer), new DateTimeOffset(2026, 11, 2, 0, 0, 0, _winter)).Should().Be(10 * 8 * 3600);

    [Fact]
    public void Between_WithAnotherSchedule_CountsItsDaysAndHours()
    {
        var weekend = Schedule([6, 7], new TimeOnly(10, 0), new TimeOnly(12, 0));

        Between(new DateTimeOffset(2026, 11, 9, 0, 0, 0, _winter), new DateTimeOffset(2026, 11, 16, 0, 0, 0, _winter), weekend).Should().Be(2 * 2 * 3600);
    }

    // Seconds are whole: a part of one doesn't count.
    [Fact]
    public void Between_PartsOfASecond_AreLeftOut() =>
        Between(Wednesday(9, 0), Wednesday(9, 0, 1).AddMilliseconds(999)).Should().Be(1);
}
