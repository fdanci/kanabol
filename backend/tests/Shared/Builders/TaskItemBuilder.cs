using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

/// <summary>
/// Builds through TaskItem.Create, so each task takes the next number of the project object given.
/// Saving the tasks without that project leaves its counter behind in the database: a test that then
/// creates a task through a handler saves the project too, or uses a project of its own.
/// </summary>
public sealed class TaskItemBuilder
{
    private static readonly DateTimeOffset _createdAt = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    private Project? _project;
    private BoardColumn? _column;
    private TaskPriority _priority = TaskPriority.Medium;
    private Guid? _assigneeId;
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

    public TaskItemBuilder AtPosition(int position)
    {
        _position = position;
        return this;
    }

    public TaskItemBuilder AssignedTo(User user)
    {
        _assigneeId = user.Id;
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
        var task = TaskItem.Create(project, _column!, _title, _description, _priority, _position, _assigneeId, _dueDate, _createdById, _createdAt);
        task.ClearDomainEvents();
        return task;
    }
}
