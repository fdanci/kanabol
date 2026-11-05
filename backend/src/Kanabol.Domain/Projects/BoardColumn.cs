using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

public sealed class BoardColumn : AuditableEntity
{
    public BoardColumn(Guid projectId, string name, int position, bool isDone)
    {
        ProjectId = projectId;
        Name = name;
        Position = position;
        IsDone = isDone;
    }

    private BoardColumn()
    {
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    public int Position { get; private set; }

    public bool IsDone { get; private set; }
}
