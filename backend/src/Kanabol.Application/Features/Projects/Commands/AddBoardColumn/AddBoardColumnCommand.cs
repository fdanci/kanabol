using Kanabol.Application.Abstractions.Messaging;

namespace Kanabol.Application.Features.Projects.Commands.AddBoardColumn;

/// <summary>A new column of the project's own, Custom (SPEC section 4.4).</summary>
public sealed record AddBoardColumnCommand(Guid ProjectId, string Name) : ICommand<BoardColumnDto>;
