using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.AddBoardColumn;

/// <summary>A new column at the right end of the board.</summary>
public sealed record AddBoardColumnCommand(Guid ProjectId, string Name, bool IsDone) : ICommand<BoardColumnDto>;
