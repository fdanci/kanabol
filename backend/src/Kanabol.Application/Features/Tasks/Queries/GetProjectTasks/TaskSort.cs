namespace Kanabol.Application.Features.Tasks.Queries.GetProjectTasks;

/// <summary>
/// How the list is sorted, written as SPEC section 7.3 has it: field:asc or field:desc, by key from the
/// newest unless asked otherwise. Ties go by number in the same direction, so the pages never overlap.
/// </summary>
public sealed record TaskSort(string Field, bool Descending)
{
    public const string Default = "key:desc";

    public const string Key = "key";
    public const string Title = "title";
    public const string Column = "column";
    public const string Priority = "priority";
    public const string DueDate = "dueDate";
    public const string Assignee = "assignee";
    public const string Updated = "updated";

    public static readonly IReadOnlySet<string> Fields = new HashSet<string>(StringComparer.Ordinal)
    {
        Key, Title, Column, Priority, DueDate, Assignee, Updated,
    };

    public static bool TryParse(string? text, out TaskSort sort)
    {
        sort = new TaskSort(Key, Descending: true);
        var parts = (text ?? "").Split(':');
        if (parts.Length != 2 || !Fields.Contains(parts[0]) || parts[1] is not ("asc" or "desc"))
        {
            return false;
        }

        sort = new TaskSort(parts[0], parts[1] == "desc");
        return true;
    }
}
