using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Abstractions;

/// <summary>Numbers a new task from its project's counter, in the database (SPEC sections 5.1 and 5.3).</summary>
public interface ITaskNumbers
{
    /// <summary>
    /// Takes the project's next number with one UPDATE … RETURNING, then saves the task that
    /// <paramref name="create"/> builds with it, in that same transaction. The UPDATE holds the project's
    /// row until the commit, so creations in one project go one after another: each gets its own number
    /// and the end of its column as it is by then, with no 409 and no retry. Returns null, with nothing
    /// taken or saved, when the project is archived by then. The domain events go out after the commit.
    /// A connection lost on the way rolls it all back, and the execution strategy runs it again, so
    /// <paramref name="create"/> may be called twice with the same number: it builds, and saves nothing.
    /// </summary>
    Task<TaskItem?> SaveNewTaskAsync(Guid projectId, Func<int, Task<TaskItem>> create, CancellationToken ct);
}
