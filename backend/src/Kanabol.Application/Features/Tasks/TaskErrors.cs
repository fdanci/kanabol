using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks;

public static class TaskErrors
{
    public static readonly Error NotFound = Error.NotFound("Task.NotFound", "This task doesn't exist.");

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
