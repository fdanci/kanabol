using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.DeleteBoardColumn;

/// <summary>
/// Only an empty column of the project's own goes, archived tasks counting; the fixed ones never (409
/// Column.Fixed, SPEC section 4.4). A task moved into the column after the check reaches the database first,
/// whose foreign key then refuses the delete, which answers the same 409 Column.NotEmpty.
/// </summary>
public sealed class DeleteBoardColumnCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    ICurrentUser currentUser,
    ILogger<DeleteBoardColumnCommandHandler> logger) : ICommandHandler<DeleteBoardColumnCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenForColumns(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var holdsTasks = await db.TaskItems.IgnoreQueryFilters().AnyAsync(task => task.ColumnId == command.ColumnId, ct);
        var change = loaded.Value.RemoveColumn(command.ColumnId, holdsTasks, currentUser.Id);
        switch (change)
        {
            case ColumnChange.NotFound:
                return ColumnErrors.NotFound;
            case ColumnChange.Fixed:
                return ColumnErrors.Fixed;
            case ColumnChange.NotEmpty:
                return ColumnErrors.NotEmpty;
        }

        try
        {
            var saved = await changes.Save(ct);
            if (saved.IsFailure)
            {
                return saved.Error;
            }
        }
        catch (DbUpdateException exception) when (exception.IsForeignKeyViolation())
        {
            return ColumnErrors.NotEmpty;
        }

        logger.LogInformation("Column {ColumnId} deleted from project {ProjectId}", command.ColumnId, command.ProjectId);
        return Unit.Value;
    }
}
