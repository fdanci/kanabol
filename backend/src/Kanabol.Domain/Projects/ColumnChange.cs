namespace Kanabol.Domain.Projects;

/// <summary>What a change to a project's columns came to. Expected refusals are answers, not exceptions.</summary>
public enum ColumnChange
{
    Done,
    NotFound,
    NotEmpty,
    LastColumn,
}
