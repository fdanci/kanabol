using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Comments.Queries.GetComment;

/// <summary>
/// One comment, where a new one's Location points, for whoever sees its task. A comment on a task the caller
/// doesn't see, such as an archived one without tasks.archive, isn't there for them.
/// </summary>
public sealed class GetCommentQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    TaskVisibility visibility) : IQueryHandler<GetCommentQuery, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(GetCommentQuery query, CancellationToken ct)
    {
        var taskId = await db.Comments
            .Where(comment => comment.Id == query.CommentId)
            .Select(comment => (Guid?)comment.TaskId)
            .FirstOrDefaultAsync(ct);
        if (taskId is null)
        {
            return CommentErrors.NotFound;
        }

        var task = await visibility.Check(taskId.Value, ct);
        if (task.IsFailure)
        {
            return task.Error == TaskErrors.NotFound ? CommentErrors.NotFound : task.Error;
        }

        return (await db.ReadComments(task.Value, currentUser.Id, ct, query.CommentId)).Single();
    }
}
