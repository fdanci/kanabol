using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// The estimate (SPEC section 4.4, "Mutarea între coloane, estimarea și pauza", group 22): whole minutes, from
/// a quarter of an hour to 1,000 hours, optional at creation, where it writes no history; every change of it
/// after writes an EstimatedMinutes entry, the minutes as text, and raises TaskUpdated.
/// </summary>
public sealed class TaskEstimateTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private TaskItem NewTask(int? estimatedMinutes = null) =>
        TaskItem.Create(
            _project,
            _project.Columns.Single(column => column.Kind == ColumnKind.Backlog),
            1,
            "Login SSO",
            null,
            TaskPriority.Medium,
            0,
            [],
            null,
            _by,
            _now,
            estimatedMinutes);

    [Fact]
    public void Create_WithAnEstimate_KeepsItAndWritesNoHistory()
    {
        var task = NewTask(estimatedMinutes: 240);

        task.EstimatedMinutes.Should().Be(240);
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void Create_WithoutAnEstimate_HasNone() => NewTask().EstimatedMinutes.Should().BeNull();

    [Theory]
    [InlineData(14)]
    [InlineData(0)]
    [InlineData(60_001)]
    public void Create_WithAnEstimateOutOfItsLimits_Throws(int minutes)
    {
        var create = () => NewTask(minutes);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(TaskItem.MinEstimateMinutes)]
    [InlineData(TaskItem.MaxEstimateMinutes)]
    public void SetEstimate_AtItsLimits_TakesIt(int minutes)
    {
        var task = NewTask();

        task.SetEstimate(minutes, _by, _now);

        task.EstimatedMinutes.Should().Be(minutes);
    }

    [Fact]
    public void SetEstimate_ToAnotherValue_WritesTheMinutesInTheHistoryAndRaisesTaskUpdated()
    {
        var task = NewTask(estimatedMinutes: 240);
        task.ClearDomainEvents();

        task.SetEstimate(360, _by, _now);

        task.EstimatedMinutes.Should().Be(360);
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { TaskId = task.Id, Field = "EstimatedMinutes", OldValue = "240", NewValue = "360", ChangedById = _by, ChangedAt = _now });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskUpdatedEvent(task.Id, _project.Id, TaskHistoryFields.EstimatedMinutes, _by));
    }

    [Fact]
    public void SetEstimate_TheFirstTimeAndToNothing_WritesBothEnds()
    {
        var task = NewTask();

        task.SetEstimate(60, _by, _now);
        task.SetEstimate(null, _by, _now.AddHours(1));

        task.EstimatedMinutes.Should().BeNull();
        task.History.Select(entry => (entry.OldValue, entry.NewValue)).Should().Equal((null, "60"), ("60", null));
    }

    [Fact]
    public void SetEstimate_ToTheSameValue_ChangesNothing()
    {
        var task = NewTask(estimatedMinutes: 90);
        task.ClearDomainEvents();

        task.SetEstimate(90, _by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(14)]
    [InlineData(60_001)]
    [InlineData(-15)]
    public void SetEstimate_OutOfItsLimits_ThrowsAndKeepsTheOldOne(int minutes)
    {
        var task = NewTask(estimatedMinutes: 90);

        var set = () => task.SetEstimate(minutes, _by, _now);

        set.Should().Throw<ArgumentOutOfRangeException>();
        task.EstimatedMinutes.Should().Be(90);
        task.History.Should().BeEmpty();
    }
}
