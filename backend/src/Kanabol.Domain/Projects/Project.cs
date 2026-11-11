using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

public sealed class Project : AuditableEntity, IArchivable
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MinKeyLength = 2;
    public const int MaxKeyLength = 5;

    private readonly List<ProjectMember> _members = [];
    private readonly List<BoardColumn> _columns = [];

    public Project(string name, string key, string? description)
    {
        var normalizedKey = NormalizeKey(key);
        if (!IsValidKey(normalizedKey))
        {
            throw new ArgumentException("A project key is 2 to 5 letters from A to Z.", nameof(key));
        }

        Name = name.Trim();
        Key = normalizedKey;
        Description = NormalizeDescription(description);
        NextTaskNumber = 1;

        // Every project has the four fixed columns, made with it (SPEC section 4.4).
        foreach (var kind in BoardColumn.FixedKinds)
        {
            _columns.Add(new BoardColumn(Id, BoardColumn.FixedName(kind), _columns.Count, kind));
        }
    }

    private Project()
    {
    }

    public string Name { get; private set; } = null!;

    /// <summary>The prefix of the task keys, as in KAN-123. It never changes, so those keys stay valid.</summary>
    public string Key { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>
    /// The number the next task gets. Numbers aren't reused, even after a task is archived. Only the
    /// database moves it, with one UPDATE … RETURNING in the transaction that saves the task, so any
    /// number of creations at once get different numbers (ITaskNumbers, SPEC sections 5.1 and 5.3).
    /// </summary>
    public int NextTaskNumber { get; private set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public Guid? ArchivedById { get; private set; }

    public IReadOnlyCollection<ProjectMember> Members => _members;

    public IReadOnlyCollection<BoardColumn> Columns => _columns;

    /// <summary>A new project, owned by whoever creates it, with the four fixed columns.</summary>
    public static Project Create(string name, string key, string? description, Guid ownerId)
    {
        var project = new Project(name, key, description);
        project.AddMember(ownerId, ProjectRole.Owner);
        return project;
    }

    /// <summary>Keys are stored in capitals, so kan and KAN can't be two projects.</summary>
    public static string NormalizeKey(string key) => key.Trim().ToUpperInvariant();

    /// <summary>Only A to Z: the key goes into addresses, such as /tasks/KAN-123.</summary>
    public static bool IsValidKey(string key)
    {
        var normalized = NormalizeKey(key);
        return normalized.Length is >= MinKeyLength and <= MaxKeyLength && normalized.All(letter => letter is >= 'A' and <= 'Z');
    }

    public void UpdateDetails(string name, string? description)
    {
        Name = name.Trim();
        Description = NormalizeDescription(description);
    }

    /// <summary>A column of the project's own, right before Gata, which stays last (SPEC section 4.4).</summary>
    public BoardColumn AddColumn(string name)
    {
        var done = _columns.Single(column => column.Kind == ColumnKind.Done);
        var column = new BoardColumn(Id, name, done.Position, ColumnKind.Custom);
        done.PlaceAt(done.Position + 1);
        _columns.Add(column);
        return column;
    }

    /// <summary>Only a column of the project's own: the fixed ones keep their names.</summary>
    public ColumnChange RenameColumn(Guid columnId, string name)
    {
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        if (column.IsFixed)
        {
            return ColumnChange.Fixed;
        }

        column.Rename(name);
        return ColumnChange.Done;
    }

    /// <summary>
    /// A column of the project's own to a place from 0, the others closing up around it: only after
    /// Backlog and before Gata, so the four fixed ones keep their order, with any number of the project's
    /// own between any two of them (SPEC section 4.4). The fixed ones never move. One holding tasks, archived ones
    /// too, stays on its side of În lucru (group 23): a started task would land before it, one not started after it.
    /// </summary>
    public ColumnChange MoveColumn(Guid columnId, int position, bool holdsTasks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        if (column.IsFixed)
        {
            return ColumnChange.Fixed;
        }

        var ordered = OrderedColumns().Where(other => other != column).ToList();
        if (position < 1 || position > ordered.Count - 1)
        {
            return ColumnChange.OutOfRange;
        }

        var inProgress = ordered.FindIndex(other => other.Kind == ColumnKind.InProgress);
        var wasBefore = column.Position < ordered[inProgress].Position;
        if (holdsTasks && wasBefore != (position <= inProgress))
        {
            return ColumnChange.CrossesInProgress;
        }

        ordered.Insert(position, column);
        Renumber(ordered);
        return ColumnChange.Done;
    }

    /// <summary>
    /// Only an empty column of the project's own goes: the fixed ones stay, so a task always has
    /// Backlog to start in. Whether it holds a task, archived ones included, isn't the project's to know,
    /// so the handler says.
    /// </summary>
    public ColumnChange RemoveColumn(Guid columnId, bool holdsTasks)
    {
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        if (column.IsFixed)
        {
            return ColumnChange.Fixed;
        }

        if (holdsTasks)
        {
            return ColumnChange.NotEmpty;
        }

        _columns.Remove(column);
        Renumber(OrderedColumns());
        return ColumnChange.Done;
    }

    public MembershipChange AddMember(Guid userId, ProjectRole role)
    {
        if (FindMember(userId) is not null)
        {
            return MembershipChange.AlreadyAMember;
        }

        _members.Add(new ProjectMember(Id, userId, role));
        return MembershipChange.Done;
    }

    /// <summary>
    /// Refused when it would leave the project without an active owner (SPEC section 4.4). Whose accounts
    /// are active isn't the project's to know, so the handler reads it and passes it in.
    /// </summary>
    public MembershipChange ChangeMemberRole(Guid userId, ProjectRole role, IReadOnlyCollection<Guid> activeMemberIds)
    {
        var member = FindMember(userId);
        if (member is null)
        {
            return MembershipChange.NotAMember;
        }

        if (member.Role == role)
        {
            return MembershipChange.Done;
        }

        if (IsTheLastActiveOwner(member, activeMemberIds))
        {
            return MembershipChange.LastOwner;
        }

        member.ChangeRole(role);
        return MembershipChange.Done;
    }

    /// <summary>Refused when it would leave the project without an active owner (SPEC section 4.4).</summary>
    public MembershipChange RemoveMember(Guid userId, IReadOnlyCollection<Guid> activeMemberIds)
    {
        var member = FindMember(userId);
        if (member is null)
        {
            return MembershipChange.NotAMember;
        }

        if (IsTheLastActiveOwner(member, activeMemberIds))
        {
            return MembershipChange.LastOwner;
        }

        _members.Remove(member);
        return MembershipChange.Done;
    }

    /// <summary>Archiving twice keeps the first time, and who did it.</summary>
    public void Archive(Guid archivedById, DateTimeOffset now)
    {
        if (ArchivedAt is null)
        {
            ArchivedAt = now;
            ArchivedById = archivedById;
        }
    }

    public void Restore()
    {
        ArchivedAt = null;
        ArchivedById = null;
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private ProjectMember? FindMember(Guid userId) => _members.Find(member => member.UserId == userId);

    private BoardColumn? FindColumn(Guid columnId) => _columns.Find(column => column.Id == columnId);

    private List<BoardColumn> OrderedColumns() => [.. _columns.OrderBy(column => column.Position)];

    private static void Renumber(List<BoardColumn> ordered)
    {
        for (var position = 0; position < ordered.Count; position++)
        {
            ordered[position].PlaceAt(position);
        }
    }

    /// <summary>
    /// Whether taking this owner away leaves no active owner behind. A deactivated owner still counts as
    /// leaving none: the project then waits for projects.manage to make a new owner first.
    /// </summary>
    private bool IsTheLastActiveOwner(ProjectMember member, IReadOnlyCollection<Guid> activeMemberIds) =>
        member.Role == ProjectRole.Owner
        && !_members.Exists(other => other.UserId != member.UserId && other.Role == ProjectRole.Owner && activeMemberIds.Contains(other.UserId));
}
