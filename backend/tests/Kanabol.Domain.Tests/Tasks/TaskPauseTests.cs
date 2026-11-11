using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// The pause's reason (SPEC section 4.4, "Mutarea între coloane, estimarea și pauza", group 22): a task enters
/// În pauză with a reason, which it keeps while it stays there and loses when it leaves; the entry writes a
/// PauseReason entry next to the move, the same moment and person, and the exit writes none.
/// </summary>
public sealed class TaskPauseTests
{
    private static readonly Guid _by = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn Kind(ColumnKind kind) => _project.Columns.Single(column => column.Kind == kind);

    private TaskItem NewTask() => new TaskItemBuilder().InProject(_project).Build();

    [Fact]
    public void MoveTo_IntoPausedWithAReason_KeepsItTrimmedAndWritesItNextToTheMove()
    {
        var task = NewTask();
        var backlog = task.ColumnId;
        task.ClearDomainEvents();

        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, _now, "  Aștept răspunsul clientului ");

        task.PauseReason.Should().Be("Aștept răspunsul clientului");
        task.History.Select(entry => (entry.Field, entry.OldValue, entry.NewValue, entry.ChangedById, entry.ChangedAt)).Should().Equal(
            (TaskHistoryFields.ColumnId, backlog.ToString(), Kind(ColumnKind.Paused).Id.ToString(), _by, _now),
            (TaskHistoryFields.PauseReason, null, "Aștept răspunsul clientului", _by, _now));
        task.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TaskMovedEvent>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MoveTo_IntoPausedWithoutAReason_Throws(string? reason)
    {
        var task = NewTask();

        var move = () => task.MoveTo(Kind(ColumnKind.Paused), 0, _by, _now, reason);

        move.Should().Throw<ArgumentException>();
        task.ColumnId.Should().NotBe(Kind(ColumnKind.Paused).Id);
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void MoveTo_OutOfPaused_ForgetsTheReasonWithoutAnEntry()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, _now, "Aștept");

        task.MoveTo(Kind(ColumnKind.InProgress), 0, _by, _now.AddHours(1));

        task.PauseReason.Should().BeNull();
        task.History.Should().HaveCount(3).And.ContainSingle(entry => entry.Field == TaskHistoryFields.PauseReason);
    }

    [Fact]
    public void MoveTo_WithinPaused_KeepsTheReasonAndAsksForNone()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, _now, "Aștept");

        task.MoveTo(Kind(ColumnKind.Paused), 3, _by, _now.AddHours(1));

        task.PauseReason.Should().Be("Aștept");
        task.Position.Should().Be(3);
    }

    // A reason given for any other column is no reason at all: it's not kept.
    [Fact]
    public void MoveTo_ElsewhereWithAReason_KeepsNone()
    {
        var task = NewTask();

        task.MoveTo(Kind(ColumnKind.Done), 0, _by, _now, "De ce?");

        task.PauseReason.Should().BeNull();
        task.History.Should().ContainSingle(entry => entry.Field == TaskHistoryFields.ColumnId);
    }

    // Archived and restored, a paused task keeps its reason (SPEC section 4.4, "Coloana Arhivate").
    [Fact]
    public void ArchiveAndRestore_KeepTheReason()
    {
        var task = NewTask();
        task.MoveTo(Kind(ColumnKind.Paused), 0, _by, _now, "Aștept");

        task.Archive(_by, _now.AddHours(1));
        task.Restore(_by, _now.AddHours(2));

        task.PauseReason.Should().Be("Aștept");
    }

    [Theory]
    [InlineData("o")]
    [InlineData("Aștept piesele de la furnizor")]
    public void IsAPauseReason_OneLineOfOneTo500Characters_IsTrue(string reason) =>
        TaskItem.IsAPauseReason(reason).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("pe două\nrânduri")]
    [InlineData("cu\ttab")]
    [InlineData("cu\u0007clopoțel")]
    public void IsAPauseReason_EmptyOrOnSeveralLinesOrWithAControlCharacter_IsFalse(string? reason) =>
        TaskItem.IsAPauseReason(reason).Should().BeFalse();

    [Fact]
    public void IsAPauseReason_Over500CharactersAfterTheTrim_IsFalse()
    {
        TaskItem.IsAPauseReason(new string('a', 500)).Should().BeTrue();
        TaskItem.IsAPauseReason("  " + new string('a', 500) + "  ").Should().BeTrue();
        TaskItem.IsAPauseReason(new string('a', 501)).Should().BeFalse();
    }
}
