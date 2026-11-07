using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks;

public static class TaskErrors
{
    public static readonly Error NotFound = Error.NotFound("Task.NotFound", "This task doesn't exist.");

    /// <summary>A comment or a file on an archived task: it's read, not changed, until someone restores it (SPEC section 4.4).</summary>
    public static readonly Error Archived = Error.Conflict("Task.Archived", "This task is archived. Restore it first.");

    public static readonly Error ChangedMeanwhile =
        Error.Conflict("Task.ChangedMeanwhile", "This task changed at the same moment. Try again.");

    public static readonly Error ColumnNotInProject = Error.Validation(new Dictionary<string, string[]>
    {
        ["ColumnId"] = ["This column isn't on the project's board."],
    });

    public static readonly Error AssigneeNotAMember = Error.Validation(new Dictionary<string, string[]>
    {
        ["AssigneeIds"] = ["Only active members of the project can be assigned."],
    });
}
