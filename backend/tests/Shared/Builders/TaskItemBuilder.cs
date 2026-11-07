using System.Runtime.CompilerServices;
using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

/// <summary>
/// Builds through TaskItem.Create. Only the database moves a project's counter, so the builder numbers
/// the tasks of each project object itself, one after another from its counter, unless WithNumber says.
/// Saving them leaves the counter behind in the database: a test that then creates a task through a
/// handler moves it past them first (TaskHandlerTest.AddTasks), or uses a project of its own.
/// </summary>
public sealed class TaskItemBuilder
{
    private static readonly DateTimeOffset _createdAt = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly ConditionalWeakTable<Project, StrongBox<int>> _lastNumbers = [];

    private int? _number;
    private Project? _project;
    private BoardColumn? _column;
    private TaskPriority _priority = TaskPriority.Medium;
    private Guid[] _assigneeIds = [];
    private DateTimeOffset? _dueDate;
    private string _title = "Task de test";
    private string? _description;
    private int _position;
    private Guid _createdById = Guid.CreateVersion7();

    public TaskItemBuilder InProject(Project project)
    {
        _project = project;
        _column ??= project.Columns.OrderBy(column => column.Position).First();
        return this;
    }

    public TaskItemBuilder InColumn(BoardColumn column)
    {
        _column = column;
        return this;
    }

    public TaskItemBuilder WithNumber(int number)
    {
        _number = number;
        return this;
    }

    public TaskItemBuilder AtPosition(int position)
    {
        _position = position;
        return this;
    }

    /// <summary>The creator makes the assignment, so a task saved with assignees needs CreatedBy a saved user too.</summary>
    public TaskItemBuilder AssignedTo(params User[] users)
    {
        _assigneeIds = [.. users.Select(user => user.Id)];
        return this;
    }

    public TaskItemBuilder CreatedBy(User user)
    {
        _createdById = user.Id;
        return this;
    }

    public TaskItemBuilder WithTitle(string title)
    {
        _title = title;
        return this;
    }

    public TaskItemBuilder WithDescription(string? description)
    {
        _description = description;
        return this;
    }

    public TaskItemBuilder WithPriority(TaskPriority priority)
    {
        _priority = priority;
        return this;
    }

    public TaskItemBuilder DueAt(DateTimeOffset dueDate)
    {
        _dueDate = dueDate;
        return this;
    }

    public TaskItem Build()
    {
        var project = _project ?? throw new InvalidOperationException("A task needs a project: call InProject first.");
        var number = _number
            ?? Interlocked.Increment(ref _lastNumbers.GetValue(project, unnumbered => new StrongBox<int>(unnumbered.NextTaskNumber - 1)).Value);
        var task = TaskItem.Create(project, _column!, number, _title, _description, _priority, _position, _assigneeIds, _dueDate, _createdById, _createdAt);
        task.ClearDomainEvents();
        return task;
    }
}
