using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Comments.Commands.EditComment;

/// <summary>
/// Only its author changes a comment, while still a real member of an open project, the task open (SPEC
/// section 4.4). An edit notifies nobody (SPEC section 4.3); UpdatedAt says it was edited.
/// </summary>
public sealed class EditCommentCommandHandler(
    IAppDbContext db,
    TaskChanges changes,
    ICurrentUser currentUser) : ICommandHandler<EditCommentCommand, CommentDto>
{
    public async Task<Result<CommentDto>> Handle(EditCommentCommand command, CancellationToken ct)
    {
        var comment = await db.Comments.FirstOrDefaultAsync(found => found.Id == command.CommentId, ct);
        if (comment is null)
        {
            return CommentErrors.NotFound;
        }

        var task = await changes.LoadToAddTo(comment.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        if (comment.AuthorId != currentUser.Id)
        {
            return CommentErrors.NotYours;
        }

        comment.Edit(command.Body);
        await db.SaveChangesAsync(ct);

        var visible = new VisibleTask(task.Value.Id, task.Value.ProjectId, TaskArchived: false, ProjectArchived: false, IsRealMember: true);
        return (await db.ReadComments(visible, currentUser.Id, ct, comment.Id)).Single();
    }
}
