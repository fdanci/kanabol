using Kanabol.Domain.Common;

namespace Kanabol.Domain.Projects;

public sealed class Project : AuditableEntity, IArchivable
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MinKeyLength = 2;
    public const int MaxKeyLength = 5;

    // The default columns, in the words the UI spec fixes (section 11).
    private static readonly (string Name, bool IsDone)[] _defaultColumns = [("De făcut", false), ("În lucru", false), ("Gata", true)];

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

    /// <summary>A new project, owned by whoever creates it, with the board's default columns.</summary>
    public static Project Create(string name, string key, string? description, Guid ownerId)
    {
        var project = new Project(name, key, description);
        project.AddMember(ownerId, ProjectRole.Owner);

        foreach (var (columnName, isDone) in _defaultColumns)
        {
            project.AddColumn(columnName, isDone);
        }

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

    /// <summary>A new column at the right end of the board.</summary>
    public BoardColumn AddColumn(string name, bool isDone)
    {
        var column = new BoardColumn(Id, name, _columns.Count, isDone);
        _columns.Add(column);
        return column;
    }

    public ColumnChange RenameColumn(Guid columnId, string name)
    {
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        column.Rename(name);
        return ColumnChange.Done;
    }

    /// <summary>
    /// Only the flag: the tasks in the column follow it through TaskItem.FollowColumn, which the handler
    /// calls, since the project doesn't hold its tasks.
    /// </summary>
    public ColumnChange MarkColumnDone(Guid columnId, bool isDone)
    {
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        column.MarkDone(isDone);
        return ColumnChange.Done;
    }

    /// <summary>To a place from 0, the others closing up around it; past the end it goes last.</summary>
    public ColumnChange MoveColumn(Guid columnId, int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        var ordered = OrderedColumns().Where(other => other != column).ToList();
        ordered.Insert(Math.Min(position, ordered.Count), column);
        Renumber(ordered);
        return ColumnChange.Done;
    }

    /// <summary>
    /// Only an empty column goes, and never the last one: a task always starts in a column. Whether it
    /// holds a task, archived ones included, isn't the project's to know, so the handler says.
    /// </summary>
    public ColumnChange RemoveColumn(Guid columnId, bool holdsTasks)
    {
        var column = FindColumn(columnId);
        if (column is null)
        {
            return ColumnChange.NotFound;
        }

        if (holdsTasks)
        {
            return ColumnChange.NotEmpty;
        }

        if (_columns.Count == 1)
        {
            return ColumnChange.LastColumn;
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
