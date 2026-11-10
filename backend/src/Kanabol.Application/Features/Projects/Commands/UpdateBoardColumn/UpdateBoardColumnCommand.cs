using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

/// <summary>The column's name. Its place changes through MoveBoardColumnCommand, and its kind never.</summary>
public sealed record UpdateBoardColumnCommand(Guid ProjectId, Guid ColumnId, string Name) : ICommand<BoardColumnDto>;
