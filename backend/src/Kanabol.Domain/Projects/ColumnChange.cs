namespace Kanabol.Domain.Projects;

/// <summary>What a change to a project's columns came to. Expected refusals are answers, not exceptions.</summary>
public enum ColumnChange
{
    Done,
    NotFound,
    NotEmpty,

    /// <summary>Backlog, În lucru, În pauză and Gata are never renamed, moved or removed.</summary>
    Fixed,

    /// <summary>A column of the project's own goes only between Backlog and Gata.</summary>
    OutOfRange,
}
