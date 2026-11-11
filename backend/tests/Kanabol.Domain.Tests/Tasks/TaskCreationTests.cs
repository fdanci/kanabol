using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Tasks.Events;
using Kanabol.Tests.Shared.Builders;

namespace Kanabol.Domain.Tests.Tasks;

/// <summary>
/// SPEC section 5.1: a task keeps the number the database took from its project's counter (ITaskNumbers),
/// which the entity never moves itself.
/// </summary>
public sealed class TaskCreationTests
{
    private static readonly Guid _creator = Guid.CreateVersion7();
    private static readonly Guid _assignee = Guid.CreateVersion7();
    private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly Project _project = new ProjectBuilder().Build();

    private BoardColumn Todo => _project.Columns.Single(column => column.Position == 0);

    private TaskItem Create(BoardColumn? column = null, Guid[]? assigneeIds = null, int number = 1) =>
        TaskItem.Create(_project, column ?? Todo, number, "  Login SSO ", "Cu **Azure AD**", TaskPriority.High, 2, assigneeIds ?? [], _now.AddDays(3), _creator, _now);

    [Fact]
    public void Create_Always_KeepsTheNumberItIsGivenAndLeavesTheCounterToTheDatabase()
    {
        var task = Create(number: 7);

        task.Number.Should().Be(7);
        _project.NextTaskNumber.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithANumberBelowOne_Throws(int number)
    {
        var create = () => Create(number: number);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Create_Always_KeepsWhatItWasGiven()
    {
        var task = Create();

        task.Should().BeEquivalentTo(new
        {
            ProjectId = _project.Id,
            ColumnId = Todo.Id,
            Number = 1,
            Title = "Login SSO",
            Description = "Cu **Azure AD**",
            Priority = TaskPriority.High,
            Position = 2,
            DueDate = _now.AddDays(3),
            CompletedAt = (DateTimeOffset?)null,
            ArchivedAt = (DateTimeOffset?)null,
        });
        task.Assignees.Should().BeEmpty();
        task.History.Should().BeEmpty();
    }

    [Fact]
    public void Create_Always_RaisesTaskCreatedWithItsNumber()
    {
        var task = Create(number: 4);

        task.DomainEvents.Should().ContainSingle().Which.Should().Be(new TaskCreatedEvent(task.Id, _project.Id, 4, _creator));
    }

    // The new assignees hear of it as with any assignment, unless they made it themselves (SPEC section 4.3).
    [Fact]
    public void Create_WithAssignees_AssignsThemAllAndRaisesTaskAssignedWithThemAll()
    {
        var other = Guid.CreateVersion7();

        var task = Create(assigneeIds: [_assignee, other]);

        task.Assignees.Should().BeEquivalentTo([
            new { TaskId = task.Id, UserId = _assignee, AssignedAt = _now, AssignedById = (Guid?)_creator },
            new { TaskId = task.Id, UserId = other, AssignedAt = _now, AssignedById = (Guid?)_creator },
        ]);
        task.History.Should().BeEmpty();
        task.DomainEvents.Should().HaveCount(2);
        task.DomainEvents.First().Should().Be(new TaskCreatedEvent(task.Id, _project.Id, 1, _creator));
        task.DomainEvents.Last().Should().BeEquivalentTo(new TaskAssignedEvent(task.Id, _project.Id, [_assignee, other], [], _creator));
    }

    [Fact]
    public void Create_WithTheSamePersonTwice_Throws()
    {
        var create = () => Create(assigneeIds: [_assignee, _assignee]);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_InADoneColumn_IsCompletedAtOnce()
    {
        var task = Create(_project.Columns.Single(column => column.Kind == ColumnKind.Done));

        task.CompletedAt.Should().Be(_now);
    }

    [Fact]
    public void Create_InAColumnOfAnotherProject_Throws()
    {
        var elsewhere = new ProjectBuilder().Build().Columns.First();

        var create = () => Create(elsewhere);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_InAnArchivedProject_Throws()
    {
        _project.Archive(_creator, _now);

        var create = () => Create();

        create.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Create_WithABlankTitle_Throws()
    {
        var create = () => TaskItem.Create(_project, Todo, 1, " ", null, TaskPriority.Low, 0, [], null, _creator, _now);

        create.Should().Throw<ArgumentException>();
    }
}
