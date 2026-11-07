using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Comments;

public static class CommentErrors
{
    public static readonly Error NotFound = Error.NotFound("Comment.NotFound", "This comment doesn't exist.");

    public static readonly Error NotYours = Error.Forbidden("Comment.NotYours", "Only its author can change a comment.");
}
