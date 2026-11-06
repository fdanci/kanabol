using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>SPEC section 5.1: a task takes its number from the project's counter, and numbers are never reused.</summary>
public sealed class TaskCreationTests
{
    private static readonly Guid _creator = Guid.CreateVersion7();
    private static readonly Guid _assignee = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn Todo => _project.Columns.Single(column => column.Position == 0);

    private TaskItem Create(BoardColumn? column = null, Guid? assigneeId = null) =>
        TaskItem.Create(_project, column ?? Todo, "  Login SSO ", "Cu **Azure AD**", TaskPriority.High, 2, assigneeId, _now.AddDays(3), _creator, _now);

    [Fact]
    public void Create_Always_TakesTheNextNumberOfTheProject()
    {
        var first = Create();
        var second = Create();

        first.Number.Should().Be(1);
        second.Number.Should().Be(2);
        _project.NextTaskNumber.Should().Be(3);
    }

    [Fact]
    public void Create_Always_KeepsWhatItWasGiven()
    {
        var task = Create();

        task.Should().BeEquivalentTo(new
        {
            ProjectId = _project.Id,
            ColumnId = Todo.Id,
            Title = "Login SSO",
            Description = "Cu **Azure AD**",
            Priority = TaskPriority.High,
            Position = 2,
            AssigneeId = (Guid?)null,
            DueDate = _now.AddDays(3),
            CompletedAt = (DateTimeOffset?)null,
            ArchivedAt = (DateTimeOffset?)null,
        });
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void Create_Always_RaisesTaskCreated()
    {
        var task = Create();

        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskCreatedEvent(task.Id, _project.Id, 1, _creator));
    }

    // The new assignee hears of it as with any assignment, unless they made it themselves (SPEC section 4.3).
    [Fact]
    public void Create_WithAnAssignee_RaisesTaskAssignedToo()
    {
        var task = Create(assigneeId: _assignee);

        task.AssigneeId.Should().Be(_assignee);
        task.DomainEvents.Should().Equal(
            new TaskCreatedEvent(task.Id, _project.Id, 1, _creator),
            new TaskAssignedEvent(task.Id, _project.Id, _assignee, null, _creator));
    }

    [Fact]
    public void Create_InADoneColumn_IsCompletedAtOnce()
    {
        var task = Create(_project.Columns.Single(column => column.IsDone));

        task.CompletedAt.Should().Be(_now);
    }

    [Fact]
    public void Create_InAColumnOfAnotherProject_ThrowsAndTakesNoNumber()
    {
        var elsewhere = new ProjectBuilder().Build().Columns.First();

        var create = () => Create(elsewhere);

        create.Should().Throw<ArgumentException>();
        _project.NextTaskNumber.Should().Be(1);
    }

    [Fact]
    public void Create_InAnArchivedProject_ThrowsAndTakesNoNumber()
    {
        _project.Archive(_creator, _now);

        var create = () => Create();

        create.Should().Throw<InvalidOperationException>();
        _project.NextTaskNumber.Should().Be(1);
    }

    [Fact]
    public void Create_WithABlankTitle_ThrowsAndTakesNoNumber()
    {
        var create = () => TaskItem.Create(_project, Todo, " ", null, TaskPriority.Low, 0, null, null, _creator, _now);

        create.Should().Throw<ArgumentException>();
        _project.NextTaskNumber.Should().Be(1);
    }

    [Fact]
    public void Number_AfterTheTaskIsArchived_IsNotGivenAgain()
    {
        var first = Create();
        first.Archive(_creator, _now);

        Create().Number.Should().Be(2);
    }
}
