using Kanabol.Application.Abstractions;
using Kanabol.Application.Features.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Comments;

public static class CommentQueries
{
    /// <summary>
    /// The task's comments, from the oldest, each with its author by name, or only the one asked for. Access
    /// is the caller's to check first, through TaskVisibility or TaskChanges.
    /// </summary>
    public static Task<CommentDto[]> ReadComments(this IAppDbContext db, VisibleTask task, Guid userId, CancellationToken ct, Guid? commentId = null) =>
        (
            from comment in db.Comments
            join author in db.Users on comment.AuthorId equals author.Id
            where comment.TaskId == task.TaskId && (commentId == null || comment.Id == commentId)
            orderby comment.CreatedAt, comment.Id
            select new CommentDto(
                comment.Id,
                comment.TaskId,
                new TaskPersonDto(author.Id, author.DisplayName),
                comment.Body,
                comment.CreatedAt,
                comment.UpdatedAt,
                task.CanAddTo && comment.AuthorId == userId))
        .AsNoTracking()
        .ToArrayAsync(ct);
}
