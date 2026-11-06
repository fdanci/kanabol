using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;

/// <summary>To a place from 0, past the end meaning last. It answers every column, since all of them may move.</summary>
public sealed record MoveBoardColumnCommand(Guid ProjectId, Guid ColumnId, int Position) : ICommand<BoardColumnDto[]>;
