using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Comments.Commands.EditComment;

public sealed record EditCommentCommand(Guid CommentId, string Body) : ICommand<CommentDto>;
