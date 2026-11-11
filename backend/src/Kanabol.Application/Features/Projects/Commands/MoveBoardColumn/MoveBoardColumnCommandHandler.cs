using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Kanabol.Application.Features.Projects.Commands.MoveBoardColumn;

/// <summary>
/// The columns stay 0, 1, 2..., and the project is saved with them, so two moves at once give one 409. A fixed
/// column answers 409 Column.Fixed, and a place before Backlog or after Gata 400 on Position (SPEC section 4.4).
/// One holding tasks, archived ones too, doesn't cross În lucru: 409 Column.CrossesInProgress (group 23). A task
/// moved into it meanwhile doesn't change the project's version, so that race of milliseconds stays, as with
/// the deletion's check before its foreign key.
/// </summary>
public sealed class MoveBoardColumnCommandHandler(IAppDbContext db, ProjectChanges changes)
    : ICommandHandler<MoveBoardColumnCommand, BoardColumnDto[]>
{
    public async Task<Result<BoardColumnDto[]>> Handle(MoveBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenForColumns(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var holdsTasks = await db.TaskItems.IgnoreQueryFilters().AnyAsync(task => task.ColumnId == command.ColumnId, ct);
        switch (loaded.Value.MoveColumn(command.ColumnId, command.Position, holdsTasks))
        {
            case ColumnChange.NotFound:
                return ColumnErrors.NotFound;
            case ColumnChange.Fixed:
                return ColumnErrors.Fixed;
            case ColumnChange.OutOfRange:
                return ColumnErrors.OutsideBacklogAndGata;
            case ColumnChange.CrossesInProgress:
                return ColumnErrors.CrossesInProgress;
        }

        var saved = await changes.Save(ct);
        return saved.IsFailure ? saved.Error : loaded.Value.ColumnsInOrder();
    }
}
