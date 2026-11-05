using Kanabol.Domain.Common;

namespace Kanabol.Domain.Tasks;

public sealed class Comment : AuditableEntity
{
    public Comment(Guid taskId, Guid authorId, string body)
    {
        TaskId = taskId;
        AuthorId = authorId;
        Body = body;
    }

    private Comment()
    {
    }

    public Guid TaskId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = null!;
}
