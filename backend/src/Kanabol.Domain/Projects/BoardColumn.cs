using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

/// <summary>A column of a project's board, changed only through its project (Project.RenameColumn and the rest).</summary>
public sealed class BoardColumn : AuditableEntity
{
    public const int MaxNameLength = 100;

    public BoardColumn(Guid projectId, string name, int position, bool isDone)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ProjectId = projectId;
        Name = NormalizeName(name);
        Position = position;
        IsDone = isDone;
    }

    private BoardColumn()
    {
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>From 0 on the left, with no gaps.</summary>
    public int Position { get; private set; }

    /// <summary>A task that enters it is completed, and one that leaves it open again (SPEC section 5.1).</summary>
    public bool IsDone { get; private set; }

    internal void Rename(string name) => Name = NormalizeName(name);

    internal void MarkDone(bool isDone) => IsDone = isDone;

    internal void PlaceAt(int position) => Position = position;

    private static string NormalizeName(string name)
    {
        var normalized = name.Trim();
        return normalized.Length > 0 ? normalized : throw new ArgumentException("A column needs a name.", nameof(name));
    }
}
