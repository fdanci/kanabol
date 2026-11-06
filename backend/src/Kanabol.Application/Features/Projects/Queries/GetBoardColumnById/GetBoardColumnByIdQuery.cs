using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Queries.GetBoardColumnById;

public sealed record GetBoardColumnByIdQuery(Guid ProjectId, Guid ColumnId) : IQuery<BoardColumnDto>;
