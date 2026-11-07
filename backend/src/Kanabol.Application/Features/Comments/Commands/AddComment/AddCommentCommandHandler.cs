using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;

namespace Kanabol.Application.Features.Comments.Commands.AddComment;

/// <summary>
/// A comment from a real member of an open project, on an open task (SPEC section 4.4). The task raises
/// CommentAddedEvent, which tells its assignees and its creator, and its board (SPEC sections 4.3 and 9).
/// </summary>
public sealed class AddCommentCommandHandler(
    IAppDbContext db,
    TaskChanges changes,
    ICurrentUser currentUser) : ICommandHandler<AddCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(AddCommentCommand command, CancellationToken ct)
    {
        var task = await changes.LoadToAddTo(command.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var comment = task.Value.AddComment(currentUser.Id, command.Body);
        await db.SaveChangesAsync(ct);

        var visible = new VisibleTask(task.Value.Id, task.Value.ProjectId, TaskArchived: false, ProjectArchived: false, IsRealMember: true);
        return (await db.ReadComments(visible, currentUser.Id, ct, comment.Id)).Single();
    }
}
