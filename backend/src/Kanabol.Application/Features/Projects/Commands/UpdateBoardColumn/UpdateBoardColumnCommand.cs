using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

/// <summary>The column's name and whether it's a done column. Its place changes through MoveBoardColumnCommand.</summary>
public sealed record UpdateBoardColumnCommand(Guid ProjectId, Guid ColumnId, string Name, bool IsDone) : ICommand<BoardColumnDto>;
