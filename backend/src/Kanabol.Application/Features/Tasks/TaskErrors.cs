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

    /// <summary>A new task starts in Backlog, whichever column the request names (SPEC section 4.4).</summary>
    public static readonly Error NotInBacklog = Error.Validation(new Dictionary<string, string[]>
    {
        ["ColumnId"] = ["A new task starts in Backlog."],
    });

    /// <summary>A task in În lucru, or in a column after it, never goes back to one before it (SPEC section 4.4).</summary>
    public static readonly Error CannotMoveBack =
        Error.Conflict("Task.CannotMoveBack", "A task started in În lucru doesn't go back to a column before it.");

    /// <summary>A task without an estimate enters În lucru only with one (SPEC section 4.4).</summary>
    public static readonly Error EstimateRequired = Error.Validation(new Dictionary<string, string[]>
    {
        ["EstimatedMinutes"] = ["A task enters În lucru with an estimate, from 15 to 60,000 minutes."],
    });

    public static readonly Error EstimateOutOfLimits = Error.Validation(new Dictionary<string, string[]>
    {
        ["EstimatedMinutes"] = ["An estimate is from 15 to 60,000 minutes."],
    });

    /// <summary>A task that entered În lucru once keeps an estimate (SPEC section 4.4).</summary>
    public static readonly Error EstimateKept = Error.Validation(new Dictionary<string, string[]>
    {
        ["EstimatedMinutes"] = ["A task that entered În lucru keeps an estimate."],
    });

    /// <summary>A task enters În pauză with its reason, one line of 1 to 500 characters (SPEC section 4.4).</summary>
    public static readonly Error PauseReasonRequired = Error.Validation(new Dictionary<string, string[]>
    {
        ["PauseReason"] = ["A task enters În pauză with a reason: one line of 1 to 500 characters."],
    });

    public static readonly Error AssigneeNotAMember = Error.Validation(new Dictionary<string, string[]>
    {
        ["AssigneeIds"] = ["Only active members of the project can be assigned."],
    });
}
