using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Common;

/// <summary>
/// What the database said when a save failed. A check made before saving can't see a request running
/// at the same moment, so the constraint is what decides, and its refusal is an answer, not a crash.
/// </summary>
public static class DatabaseErrors
{
    private const string UniqueViolation = "23505";
    private const string ForeignKeyViolation = "23503";

    public static bool IsUniqueViolation(this DbUpdateException exception) => HasState(exception, UniqueViolation);

    public static bool IsForeignKeyViolation(this DbUpdateException exception) => HasState(exception, ForeignKeyViolation);

    private static bool HasState(DbUpdateException exception, string sqlState) =>
        exception.InnerException is DbException { SqlState: var state } && state == sqlState;
}
