using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetBoardColumns;

public sealed record GetBoardColumnsQuery(Guid ProjectId) : IQuery<BoardColumnDto[]>;
