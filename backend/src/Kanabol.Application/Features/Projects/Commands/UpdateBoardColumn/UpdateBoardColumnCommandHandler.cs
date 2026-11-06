using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

/// <summary>
/// A column that becomes a done column completes the open tasks in it, now, and one that stops being one
/// opens them again, as if they had just entered it (SPEC section 5.1). Those tasks are saved with their
/// versions, so a task changed at the same moment makes this a 409 that changes nothing.
/// </summary>
public sealed class UpdateBoardColumnCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    IClock clock,
    ILogger<UpdateBoardColumnCommandHandler> logger) : ICommandHandler<UpdateBoardColumnCommand, BoardColumnDto>
{
    public async Task<Result<BoardColumnDto>> Handle(UpdateBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var project = loaded.Value;
        if (project.RenameColumn(command.ColumnId, command.Name) == ColumnChange.NotFound)
        {
            return ColumnErrors.NotFound;
        }

        var column = project.Columns.Single(found => found.Id == command.ColumnId);
        var turned = column.IsDone != command.IsDone;
        if (turned)
        {
            project.MarkColumnDone(column.Id, command.IsDone);
            var now = clock.UtcNow;
            var tasks = await db.TaskItems.Where(task => task.ColumnId == column.Id).ToListAsync(ct);
            tasks.ForEach(task => task.FollowColumn(column, now));
        }

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        if (turned)
        {
            logger.LogInformation(
                "Column {ColumnId} of project {ProjectId} is a done column: {IsDone}",
                column.Id,
                command.ProjectId,
                column.IsDone);
        }

        return column.ToDto();
    }
}
