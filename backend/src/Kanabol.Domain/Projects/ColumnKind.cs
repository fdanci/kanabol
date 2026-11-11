namespace Kanabol.Domain.Projects;

/// <summary>
/// What a board column means (SPEC section 4.4, "Coloanele fixe"): each project has the four fixed kinds
/// once, in this order, and any number of its own, Custom, between Backlog and Done. Stored as text.
/// </summary>
public enum ColumnKind
{
    Backlog,
    InProgress,
    Paused,

    /// <summary>A task that enters it is completed, and one that leaves it open again.</summary>
    Done,
    Custom,
}
