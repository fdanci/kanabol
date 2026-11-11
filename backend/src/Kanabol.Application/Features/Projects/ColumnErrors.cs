using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects;

public static class ColumnErrors
{
    public static readonly Error NotFound = Error.NotFound("Column.NotFound", "This column isn't on the project's board.");

    /// <summary>Archived tasks count too: they keep their column, and come back to it when restored.</summary>
    public static readonly Error NotEmpty =
        Error.Conflict("Column.NotEmpty", "This column still holds tasks, archived ones included. Move them out first.");

    /// <summary>Backlog, În lucru, În pauză and Gata, which every board has (SPEC section 4.4).</summary>
    public static readonly Error Fixed =
        Error.Conflict("Column.Fixed", "Backlog, În lucru, În pauză and Gata are never renamed, moved or deleted.");

    /// <summary>
    /// A column holding tasks, archived ones too, doesn't cross În lucru: its started tasks would land before it,
    /// or its tasks not started after it, without a move (SPEC section 4.4, group 23).
    /// </summary>
    public static readonly Error CrossesInProgress = Error.Conflict(
        "Column.CrossesInProgress",
        "A column holding tasks, archived ones included, doesn't cross În lucru. Move its tasks out first.");

    /// <summary>Backlog stays first and Gata last, so a column of the project's own goes only between them.</summary>
    public static readonly Error OutsideBacklogAndGata = Error.Validation(new Dictionary<string, string[]>
    {
        ["Position"] = ["A column of the project's own goes after Backlog and before Gata."],
    });
}
