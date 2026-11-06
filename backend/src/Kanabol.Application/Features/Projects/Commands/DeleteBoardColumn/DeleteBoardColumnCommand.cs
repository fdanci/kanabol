using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;

namespace Kanabol.Application.Features.Projects.Commands.DeleteBoardColumn;

public sealed record DeleteBoardColumnCommand(Guid ProjectId, Guid ColumnId) : ICommand<Unit>;
