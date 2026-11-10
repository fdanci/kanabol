using Kanabol.Domain.Settings;

namespace Kanabol.Domain.Tests.Settings;

/// <summary>
/// The work schedule (SPEC sections 4.4, "Setările generale", and 5.1, group 22): the working days as ISO numbers,
/// 1 for Monday to 7 for Sunday, at least one and each once, and the hours, the start before the end; Monday to
/// Friday, 08:30 to 16:30 unless changed.
/// </summary>
public sealed class WorkScheduleTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Default_IsMondayToFridayFrom0830To1630()
    {
        var schedule = WorkSchedule.Default();

        schedule.Days.Should().Equal(1, 2, 3, 4, 5);
        schedule.WorkDays.Should().Be("1,2,3,4,5");
        (schedule.StartTime, schedule.EndTime).Should().Be((new TimeOnly(8, 30), new TimeOnly(16, 30)));
        schedule.UpdatedAt.Should().BeNull();
        schedule.UpdatedById.Should().BeNull();
    }

    [Fact]
    public void Change_Always_KeepsTheDaysInOrderAndWhoChangedItWhen()
    {
        var schedule = WorkSchedule.Default();

        schedule.Change([6, 1, 3], new TimeOnly(9, 0), new TimeOnly(17, 15), _by, _now);

        schedule.Days.Should().Equal(1, 3, 6);
        schedule.WorkDays.Should().Be("1,3,6");
        (schedule.StartTime, schedule.EndTime).Should().Be((new TimeOnly(9, 0), new TimeOnly(17, 15)));
        (schedule.UpdatedAt, schedule.UpdatedById).Should().Be((_now, _by));
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0 })]
    [InlineData(new[] { 8 })]
    [InlineData(new[] { 1, 1 })]
    public void Change_WithoutDaysOrWithAWrongOneOrOneTwice_Throws(int[] days)
    {
        var schedule = WorkSchedule.Default();

        var change = () => schedule.Change(days, new TimeOnly(9, 0), new TimeOnly(17, 0), _by, _now);

        change.Should().Throw<ArgumentException>();
        schedule.Days.Should().Equal(1, 2, 3, 4, 5);
    }

    [Theory]
    [InlineData(9, 0, 9, 0)]
    [InlineData(17, 0, 9, 0)]
    public void Change_WithTheStartNotBeforeTheEnd_Throws(int startHour, int startMinute, int endHour, int endMinute)
    {
        var schedule = WorkSchedule.Default();

        var change = () => schedule.Change([1], new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute), _by, _now);

        change.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, true)]
    [InlineData(new[] { 7 }, true)]
    [InlineData(new[] { 1, 2, 3, 4, 5, 6, 7 }, true)]
    [InlineData(new int[0], false)]
    [InlineData(new[] { 1, 9 }, false)]
    [InlineData(new[] { 2, 2 }, false)]
    public void AreWorkDays_SaysWhetherTheDaysAreAtLeastOneEachOnceFrom1To7(int[] days, bool valid) =>
        WorkSchedule.AreWorkDays(days).Should().Be(valid);

    // ISO numbers: Monday is 1 and Sunday 7, where DayOfWeek has Sunday 0.
    [Theory]
    [InlineData(DayOfWeek.Monday, true)]
    [InlineData(DayOfWeek.Friday, true)]
    [InlineData(DayOfWeek.Saturday, false)]
    [InlineData(DayOfWeek.Sunday, false)]
    public void IsAWorkDay_ByDefault_IsMondayToFriday(DayOfWeek day, bool works) =>
        WorkSchedule.Default().IsAWorkDay(day).Should().Be(works);

    [Fact]
    public void IsAWorkDay_OfSunday_IsSevenInTheList()
    {
        var schedule = WorkSchedule.Default();
        schedule.Change([7], new TimeOnly(10, 0), new TimeOnly(12, 0), _by, _now);

        schedule.IsAWorkDay(DayOfWeek.Sunday).Should().BeTrue();
        schedule.IsAWorkDay(DayOfWeek.Monday).Should().BeFalse();
    }
}
