using Kanabol.Domain.Projects;
using Kanabol.Domain.Tasks;
using Kanabol.Domain.Users;

namespace Kanabol.Tests.Shared.Builders;

public sealed class TaskItemBuilder
{
    private Guid _projectId = Guid.CreateVersion7();
    private Guid _columnId = Guid.CreateVersion7();
    private TaskPriority _priority = TaskPriority.Medium;
    private Guid? _assigneeId;
    private DateTimeOffset? _dueDate;
    private string _title = "Task de test";

    public TaskItemBuilder InProject(Project project)
    {
        _projectId = project.Id;
        _columnId = project.Columns.First().Id;
        return this;
    }

    public TaskItemBuilder InColumn(BoardColumn column)
    {
        _columnId = column.Id;
        return this;
    }

    public TaskItemBuilder AssignedTo(User user)
    {
        _assigneeId = user.Id;
        return this;
    }

    public TaskItemBuilder WithTitle(string title)
    {
        _title = title;
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

    public TaskItem Build() =>
        new(_projectId, _columnId, _title, description: null, _priority, position: 0, _assigneeId, _dueDate);
}
