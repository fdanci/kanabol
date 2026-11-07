using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Comments.Queries.GetTaskComments;

public sealed record GetTaskCommentsQuery(Guid TaskId) : IQuery<CommentDto[]>;
