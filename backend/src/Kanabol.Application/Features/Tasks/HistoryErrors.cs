using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Tasks;

public static class HistoryErrors
{
    /// <summary>An entry that isn't in this task's history, another task's among them (SPEC section 4.4).</summary>
    public static readonly Error NotFound = Error.NotFound("History.NotFound", "This change isn't in the task's history.");
}
