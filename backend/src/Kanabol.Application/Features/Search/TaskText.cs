using System.Linq.Expressions;
using Kanabol.Domain.Tasks;

namespace Kanabol.Application.Features.Search;

/// <summary>
/// The text of a task that the model turns into a vector (SPEC section 19.2): its title, an empty line and its
/// description, or the title alone when it has none. It's composed in SQL, so the same expression finds the tasks
/// whose stored text no longer matches and gives the text to send.
/// </summary>
public sealed class TaskText
{
    public const string Separator = "\n\n";

    /// <summary>A task's text, as a query projects it. A description is never empty: a blank one is stored as none.</summary>
    public static readonly Expression<Func<TaskItem, TaskText>> Of = task => new TaskText
    {
        TaskId = task.Id,
        Text = task.Description == null ? task.Title : task.Title + Separator + task.Description,
    };

    public Guid TaskId { get; init; }

    public string Text { get; init; } = "";
}
