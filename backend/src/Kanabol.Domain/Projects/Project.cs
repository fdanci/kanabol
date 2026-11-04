using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

public sealed class Project : AuditableEntity, IArchivable
{
    private readonly List<ProjectMember> _members = [];
    private readonly List<BoardColumn> _columns = [];

    public Project(string name, string? description)
    {
        Name = name;
        Description = description;
    }

    private Project()
    {
    }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedById { get; private set; }

    public IReadOnlyCollection<ProjectMember> Members => _members;

    public IReadOnlyCollection<BoardColumn> Columns => _columns;

    public void AddMember(Guid userId, ProjectRole role) => _members.Add(new ProjectMember(Id, userId, role));

    public BoardColumn AddColumn(string name, int position, bool isDone)
    {
        var column = new BoardColumn(Id, name, position, isDone);
        _columns.Add(column);
        return column;
    }
}
