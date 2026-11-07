using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

/// <summary>
/// A comment on a task (SPEC section 4.4): basic markdown, like the task's description, kept as written but
/// for its blank end. Only its author changes it, and nothing deletes it in v1. It comes through
/// TaskItem.AddComment, which tells whoever follows the task.
/// </summary>
public sealed class Comment : AuditableEntity
{
    /// <summary>A comment, not a document: about two pages of text.</summary>
    public const int MaxBodyLength = 5_000;

    public Comment(Guid taskId, Guid authorId, string body)
    {
        TaskId = taskId;
        AuthorId = authorId;
        Body = Normalize(body);
    }

    private Comment()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>Basic markdown, as written. The client renders it through a sanitizer (SPEC section 16).</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Whether the caller is its author is the handler's to check. The same text changes nothing.</summary>
    public void Edit(string body) => Body = Normalize(body);

    // Leading spaces mean something in markdown, such as a code block, so only the blank end goes.
    private static string Normalize(string body)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return body.TrimEnd();
    }
}
