using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Comments.Commands.AddComment;

public sealed record AddCommentCommand(Guid TaskId, string Body) : ICommand<CommentDto>;
