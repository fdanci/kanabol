using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Comments.Queries.GetComment;

public sealed record GetCommentQuery(Guid CommentId) : IQuery<CommentDto>;
