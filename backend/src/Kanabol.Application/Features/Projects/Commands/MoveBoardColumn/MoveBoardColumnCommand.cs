using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;

/// <summary>
/// A column of the project's own to a place from 0, after Backlog and before Gata. It answers every column,
/// since all of them may move.
/// </summary>
public sealed record MoveBoardColumnCommand(Guid ProjectId, Guid ColumnId, int Position) : ICommand<BoardColumnDto[]>;
