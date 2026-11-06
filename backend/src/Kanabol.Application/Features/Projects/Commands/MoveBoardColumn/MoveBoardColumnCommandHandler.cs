using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;

/// <summary>The columns stay 0, 1, 2..., and the project is saved with them, so two moves at once give one 409.</summary>
public sealed class MoveBoardColumnCommandHandler(ProjectChanges changes) : ICommandHandler<MoveBoardColumnCommand, BoardColumnDto[]>
{
    public async Task<Result<BoardColumnDto[]>> Handle(MoveBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (loaded.Value.MoveColumn(command.ColumnId, command.Position) == ColumnChange.NotFound)
        {
            return ColumnErrors.NotFound;
        }

        var saved = await changes.Save(ct);
        return saved.IsFailure ? saved.Error : loaded.Value.ColumnsInOrder();
    }
}
