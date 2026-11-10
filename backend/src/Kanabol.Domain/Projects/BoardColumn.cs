using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

/// <summary>A column of a project's board, changed only through its project (Project.RenameColumn and the rest).</summary>
public sealed class BoardColumn : AuditableEntity
{
    public const int MaxNameLength = 100;

    // The fixed columns, in their order and in the words the UI spec fixes (section 11).
    private static readonly (ColumnKind Kind, string Name)[] _fixed =
    [
        (ColumnKind.Backlog, "Backlog"),
        (ColumnKind.InProgress, "În lucru"),
        (ColumnKind.Paused, "În pauză"),
        (ColumnKind.Done, "Gata"),
    ];

    public BoardColumn(Guid projectId, string name, int position, ColumnKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ProjectId = projectId;
        Name = NormalizeName(name);
        Position = position;
        Kind = kind;
    }

    private BoardColumn()
    {
    }

    /// <summary>The four kinds every project has once, from the left.</summary>
    public static IReadOnlyList<ColumnKind> FixedKinds { get; } = [.. _fixed.Select(column => column.Kind)];

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>From 0 on the left, with no gaps.</summary>
    public int Position { get; private set; }

    public ColumnKind Kind { get; private set; }

    /// <summary>Backlog, În lucru, În pauză or Gata, which every project has; Custom ones are the project's own.</summary>
    public bool IsFixed => Kind != ColumnKind.Custom;

    /// <summary>The name a fixed column always has.</summary>
    public static string FixedName(ColumnKind kind) =>
        _fixed.Single(column => column.Kind == kind).Name;

    /// <summary>Whether the name is one of the four, in any case: a column of the project's own can't take it.</summary>
    public static bool IsAFixedName(string name) =>
        _fixed.Any(column => string.Equals(column.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    internal void Rename(string name) => Name = NormalizeName(name);

    internal void PlaceAt(int position) => Position = position;

    private static string NormalizeName(string name)
    {
        var normalized = name.Trim();
        return normalized.Length > 0 ? normalized : throw new ArgumentException("A column needs a name.", nameof(name));
    }
}
