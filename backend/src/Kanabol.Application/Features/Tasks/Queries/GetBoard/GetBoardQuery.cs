using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Tasks.Queries.GetBoard;

public sealed record GetBoardQuery(Guid ProjectId) : IQuery<BoardDto>;
