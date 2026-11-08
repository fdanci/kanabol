using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// SPEC section 5.1: each change writes its own history entry, so the history doesn't depend on the
/// handler, and raises a domain event. A change to the same value changes nothing, and writes nothing.
/// </summary>
public sealed class TaskItemTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly Guid _assignee = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn Todo => _project.Columns.Single(column => column.Position == 0);

    private BoardColumn InProgress => _project.Columns.Single(column => column.Position == 1);

    private BoardColumn Done => _project.Columns.Single(column => column.IsDone);

    private TaskItem NewTask() => new TaskItemBuilder().InProject(_project).WithTitle("Login SSO").Build();

    [Fact]
    public void Rename_WhenTheTitleChanges_WritesTheHistoryAndRaisesTaskUpdated()
    {
        var task = NewTask();

        task.Rename("  Login cu SSO ", _by, _now);

        task.Title.Should().Be("Login cu SSO");
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { TaskId = task.Id, Field = "Title", OldValue = "Login SSO", NewValue = "Login cu SSO", ChangedById = _by, ChangedAt = _now });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskUpdatedEvent(task.Id, _project.Id, "Title", _by));
    }

    [Fact]
    public void Rename_WhenTheTitleIsTheSame_ChangesNothing()
    {
        var task = NewTask();

        task.Rename(" Login SSO ", _by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void Rename_WhenBlank_ThrowsAndKeepsTheTitle(string title)
    {
        var task = NewTask();

        var rename = () => task.Rename(title, _by, _now);

        rename.Should().Throw<ArgumentException>();
        task.Title.Should().Be("Login SSO");
    }

    [Fact]
    public void ChangeDescription_WhenItChanges_WritesTheHistoryAndRaisesTaskUpdated()
    {
        var task = NewTask();

        task.ChangeDescription("Cu **Azure AD**", _by, _now);
        task.ChangeDescription("Cu Google", _by, _now.AddMinutes(1));

        task.Description.Should().Be("Cu Google");
        task.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue)).Should().Equal(
            ("Description", null, "Cu **Azure AD**"),
            ("Description", "Cu **Azure AD**", "Cu Google"));
        task.DomainEvents.Should().AllBeEquivalentTo(new TaskUpdatedEvent(task.Id, _project.Id, "Description", _by));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    public void ChangeDescription_WhenBlank_ClearsItAndWritesTheHistory(string? description)
    {
        var task = NewTask();
        task.ChangeDescription("Ceva", _by, _now);

        task.ChangeDescription(description, _by, _now);

        task.Description.Should().BeNull();
        task.History.Last().Should().BeEquivalentTo(new { Field = "Description", OldValue = "Ceva", NewValue = (string?)null });
    }

    [Fact]
    public void ChangeDescription_Always_KeepsTheIndentOfMarkdownButNotTheBlankEnd()
    {
        var task = NewTask();

        task.ChangeDescription("    cod\n- punct  \n\n", _by, _now);

        task.Description.Should().Be("    cod\n- punct");
    }

    [Fact]
    public void ChangeDescription_WhenTheSame_ChangesNothing()
    {
        var task = NewTask();
        task.ChangeDescription("Ceva", _by, _now);
        task.ClearDomainEvents();

        task.ChangeDescription("Ceva\n", _by, _now);

        task.History.Should().ContainSingle();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Assign_WhenSomeoneNew_WritesTheHistoryAndRaisesTaskAssigned()
    {
        var task = NewTask();

        task.Assign([_assignee], _by, _now);

        task.Assignees.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { TaskId = task.Id, UserId = _assignee, AssignedAt = _now, AssignedById = (Guid?)_by });
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Field = "Assignees", OldValue = "", NewValue = _assignee.ToString(), ChangedById = _by });
        task.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TaskAssignedEvent(task.Id, _project.Id, [_assignee], [], _by));
    }

    // SPEC section 5.1: one entry per change, the old list and the new one, sorted and separated by commas.
    [Fact]
    public void Assign_TwoPeople_WritesOneEntryWithBothSortedAndRaisesOneEventWithBoth()
    {
        var task = NewTask();
        var (first, second) = Sorted(Guid.NewGuid(), Guid.NewGuid());

        task.Assign([second, first], _by, _now);

        task.Assignees.Select(assignee => assignee.UserId).Should().BeEquivalentTo([first, second]);
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Field = "Assignees", OldValue = "", NewValue = $"{first},{second}" });
        task.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TaskAssignedEvent>()
            .Which.AddedIds.Should().BeEquivalentTo([first, second]);
    }

    [Fact]
    public void Assign_OneOutAndOneIn_KeepsWhenTheOneWhoStaysWasAssignedAndTellsWhoCameAndWhoLeft()
    {
        var task = NewTask();
        var (leaving, staying, coming) = Sorted(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var earlier = _now.AddDays(-2);
        task.Assign([leaving, staying], Guid.NewGuid(), earlier);
        task.ClearDomainEvents();

        task.Assign([staying, coming], _by, _now);

        task.Assignees.Should().BeEquivalentTo([
            new { UserId = staying, AssignedAt = earlier },
            new { UserId = coming, AssignedAt = _now },
        ]);
        task.History.Last().Should().BeEquivalentTo(
            new { Field = "Assignees", OldValue = $"{leaving},{staying}", NewValue = $"{staying},{coming}", ChangedAt = _now });
        task.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TaskAssignedEvent(task.Id, _project.Id, [coming], [leaving], _by));
    }

    [Fact]
    public void Assign_ToNobody_WritesAnEmptyListAndRaisesTaskAssignedWithNobodyAdded()
    {
        var task = new TaskItemBuilder().InProject(_project).Build();
        task.Assign([_assignee], _by, _now);
        task.ClearDomainEvents();

        task.Assign([], _by, _now);

        task.Assignees.Should().BeEmpty();
        task.History.Last().Should().BeEquivalentTo(new { Field = "Assignees", OldValue = _assignee.ToString(), NewValue = "" });
        task.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TaskAssignedEvent(task.Id, _project.Id, [], [_assignee], _by));
    }

    [Fact]
    public void Assign_TheSamePeopleInAnotherOrder_ChangesNothing()
    {
        var task = NewTask();
        var other = Guid.NewGuid();
        task.Assign([_assignee, other], _by, _now);
        task.ClearDomainEvents();

        task.Assign([other, _assignee], _by, _now.AddHours(1));

        task.History.Should().ContainSingle();
        task.Assignees.Should().OnlyContain(assignee => assignee.AssignedAt == _now);
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Assign_NobodyWhenNobodyIs_ChangesNothing()
    {
        var task = NewTask();

        task.Assign([], _by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    // The validators refuse it first, with a 400 on assigneeIds (SPEC section 4.4).
    [Fact]
    public void Assign_TheSamePersonTwice_Throws()
    {
        var task = NewTask();

        var assign = () => task.Assign([_assignee, _assignee], _by, _now);

        assign.Should().Throw<ArgumentException>();
        task.Assignees.Should().BeEmpty();
    }

    [Fact]
    public void SetPriority_WhenItChanges_WritesTheNamesAndRaisesTaskUpdated()
    {
        var task = NewTask();

        task.SetPriority(TaskPriority.Critical, _by, _now);

        task.Priority.Should().Be(TaskPriority.Critical);
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Field = "Priority", OldValue = "Medium", NewValue = "Critical" });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskUpdatedEvent(task.Id, _project.Id, "Priority", _by));
    }

    [Fact]
    public void SetPriority_WhenTheSame_ChangesNothing()
    {
        var task = NewTask();

        task.SetPriority(TaskPriority.Medium, _by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetDueDate_WhenItChanges_WritesTheDatesInRoundTripFormAndRaisesTaskUpdated()
    {
        var task = NewTask();
        var due = new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

        task.SetDueDate(due, _by, _now);
        task.SetDueDate(null, _by, _now);

        task.DueDate.Should().BeNull();
        task.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue)).Should().Equal(
            ("DueDate", null, "2026-11-20T15:00:00.0000000+00:00"),
            ("DueDate", "2026-11-20T15:00:00.0000000+00:00", null));
        task.DomainEvents.Should().HaveCount(2).And.AllBeEquivalentTo(new TaskUpdatedEvent(task.Id, _project.Id, "DueDate", _by));
    }

    [Fact]
    public void SetDueDate_WhenTheSameMomentInAnotherOffset_ChangesNothing()
    {
        var task = NewTask();
        task.SetDueDate(new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero), _by, _now);
        task.ClearDomainEvents();

        task.SetDueDate(new DateTimeOffset(2026, 11, 20, 17, 0, 0, TimeSpan.FromHours(2)), _by, _now);

        task.History.Should().ContainSingle();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MoveTo_AnotherColumn_WritesTheColumnsAndRaisesTaskMoved()
    {
        var task = NewTask();

        task.MoveTo(InProgress, 3, _by, _now);

        task.ColumnId.Should().Be(InProgress.Id);
        task.Position.Should().Be(3);
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Field = "ColumnId", OldValue = Todo.Id.ToString(), NewValue = InProgress.Id.ToString(), ChangedById = _by, ChangedAt = _now });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskMovedEvent(task.Id, _project.Id, Todo.Id, InProgress.Id, 3, _by));
    }

    [Fact]
    public void MoveTo_ADoneColumn_CompletesTheTask()
    {
        var task = NewTask();

        task.MoveTo(Done, 0, _by, _now);

        task.CompletedAt.Should().Be(_now);
    }

    [Fact]
    public void MoveTo_WithinTheDoneColumn_KeepsWhenItWasCompleted()
    {
        var task = NewTask();
        task.MoveTo(Done, 0, _by, _now);

        task.MoveTo(Done, 2, _by, _now.AddDays(1));

        task.CompletedAt.Should().Be(_now);
    }

    [Fact]
    public void MoveTo_OutOfTheDoneColumn_ClearsTheCompletion()
    {
        var task = NewTask();
        task.MoveTo(Done, 0, _by, _now);

        task.MoveTo(InProgress, 0, _by, _now.AddDays(1));

        task.CompletedAt.Should().BeNull();
    }

    // A reorder isn't a change of state: the history keeps the columns, which the reports read (SPEC 5.1.1).
    [Fact]
    public void MoveTo_WithinTheSameColumn_RaisesTaskMovedWithoutHistory()
    {
        var task = NewTask();

        task.MoveTo(Todo, 4, _by, _now);

        task.Position.Should().Be(4);
        task.History.Should().BeEmpty();
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskMovedEvent(task.Id, _project.Id, Todo.Id, Todo.Id, 4, _by));
    }

    [Fact]
    public void MoveTo_TheSamePlace_ChangesNothing()
    {
        var task = NewTask();

        task.MoveTo(Todo, 0, _by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MoveTo_AColumnOfAnotherProject_Throws()
    {
        var task = NewTask();
        var elsewhere = new ProjectBuilder().Build().Columns.First();

        var move = () => task.MoveTo(elsewhere, 0, _by, _now);

        move.Should().Throw<ArgumentException>();
        task.ColumnId.Should().Be(Todo.Id);
    }

    [Fact]
    public void MoveTo_ANegativePosition_Throws()
    {
        var move = () => NewTask().MoveTo(InProgress, -1, _by, _now);

        move.Should().Throw<ArgumentOutOfRangeException>();
    }

    // What a move does to the tasks around it isn't a change of theirs: no history, no event.
    [Fact]
    public void ShiftTo_AnotherPlace_MovesItQuietly()
    {
        var task = NewTask();

        task.ShiftTo(3);

        task.Position.Should().Be(3);
        task.ColumnId.Should().Be(Todo.Id);
        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ShiftTo_ANegativePlace_Throws()
    {
        var shift = () => NewTask().ShiftTo(-1);

        shift.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Archive_Always_RecordsWhenAndWhoWritesTheHistoryAndRaisesTaskArchived()
    {
        var task = NewTask();

        task.Archive(_by, _now);

        task.ArchivedAt.Should().Be(_now);
        task.ArchivedById.Should().Be(_by);
        task.History.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { Field = "ArchivedAt", OldValue = (string?)null, NewValue = "2026-11-06T12:00:00.0000000+00:00", ChangedById = _by });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskArchivedEvent(task.Id, _project.Id, _by));
    }

    /// <summary>
    /// PostgreSQL keeps a timestamptz to the microsecond, so a restore, which writes the moment read back,
    /// writes the very text the archiving wrote: the entity writes dates to the microsecond (group 16).
    /// </summary>
    [Fact]
    public void Archive_AtAMomentFinerThanAMicrosecond_WritesItToTheMicrosecond()
    {
        var task = NewTask();
        var stored = _now.AddTicks(12_345_670);

        task.Archive(_by, stored.AddTicks(8));

        task.History.Single().NewValue.Should().Be("2026-11-06T12:00:01.2345670+00:00");
    }

    [Fact]
    public void SetDueDate_AtAMomentFinerThanAMicrosecond_WritesItToTheMicrosecond()
    {
        var task = NewTask();

        task.SetDueDate(new DateTimeOffset(2026, 11, 20, 21, 59, 59, TimeSpan.Zero).AddTicks(9_999_999), _by, _now);

        task.History.Single().NewValue.Should().Be("2026-11-20T21:59:59.9999990+00:00");
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_KeepsTheFirstTimeAndWritesNothing()
    {
        var task = NewTask();
        var other = Guid.CreateVersion7();
        task.Archive(_by, _now);
        task.ClearDomainEvents();

        task.Archive(other, _now.AddHours(1));

        task.ArchivedAt.Should().Be(_now);
        task.ArchivedById.Should().Be(_by);
        task.History.Should().ContainSingle();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Restore_WhenArchived_ClearsTheArchiveWritesTheHistoryAndRaisesTaskRestored()
    {
        var task = NewTask();
        task.Archive(_by, _now);
        task.ClearDomainEvents();
        var restorer = Guid.CreateVersion7();

        task.Restore(restorer, _now.AddDays(1));

        task.ArchivedAt.Should().BeNull();
        task.ArchivedById.Should().BeNull();
        task.History.Last().Should().BeEquivalentTo(
            new { Field = "ArchivedAt", OldValue = "2026-11-06T12:00:00.0000000+00:00", NewValue = (string?)null, ChangedById = restorer });
        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskRestoredEvent(task.Id, _project.Id, restorer));
    }

    [Fact]
    public void Restore_WhenNotArchived_ChangesNothing()
    {
        var task = NewTask();

        task.Restore(_by, _now);

        task.History.Should().BeEmpty();
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Changes_InARow_EachWriteTheirOwnEntryInOrder()
    {
        var task = NewTask();

        task.Rename("Login cu SSO", _by, _now);
        task.SetPriority(TaskPriority.High, _by, _now);
        task.Assign([_assignee], _by, _now);
        task.SetDueDate(_now.AddDays(7), _by, _now);
        task.ChangeDescription("Detalii", _by, _now);
        task.MoveTo(InProgress, 0, _by, _now);
        task.Archive(_by, _now);
        task.Restore(_by, _now);

        task.History.Select(entry => entry.Field).Should().Equal(
            "Title", "Priority", "Assignees", "DueDate", "Description", "ColumnId", "ArchivedAt", "ArchivedAt");
        task.History.Should().OnlyContain(entry => entry.TaskId == task.Id);
        task.DomainEvents.Should().HaveCount(8);
    }

    // The history sorts the ids as text, so the tests name them in that order.
    private static (Guid First, Guid Second) Sorted(Guid one, Guid two)
    {
        var sorted = new[] { one, two }.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        return (sorted[0], sorted[1]);
    }

    private static (Guid First, Guid Second, Guid Third) Sorted(Guid one, Guid two, Guid three)
    {
        var sorted = new[] { one, two, three }.OrderBy(id => id.ToString(), StringComparer.Ordinal).ToArray();
        return (sorted[0], sorted[1], sorted[2]);
    }
}
