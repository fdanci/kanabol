using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;

namespace Kanabol.Application.Features.Comments.Queries.GetTaskComments;

/// <summary>Every comment of the task, from the oldest, without pages, for whoever sees it (SPEC section 4.4).</summary>
public sealed class GetTaskCommentsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    TaskVisibility visibility) : IQueryHandler<GetTaskCommentsQuery, CommentDto[]>
{
    public async Task<Result<CommentDto[]>> Handle(GetTaskCommentsQuery query, CancellationToken ct)
    {
        var task = await visibility.Check(query.TaskId, ct);
        return task.IsFailure ? task.Error : await db.ReadComments(task.Value, currentUser.Id, ct);
    }
}
