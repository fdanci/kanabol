using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects;

public static class ColumnErrors
{
    public static readonly Error NotFound = Error.NotFound("Column.NotFound", "This column isn't on the project's board.");

    /// <summary>Archived tasks count too: they keep their column, and come back to it when restored.</summary>
    public static readonly Error NotEmpty =
        Error.Conflict("Column.NotEmpty", "This column still holds tasks, archived ones included. Move them out first.");

    public static readonly Error LastColumn =
        Error.Conflict("Column.LastColumn", "A board needs a column: every task starts in one.");
}
