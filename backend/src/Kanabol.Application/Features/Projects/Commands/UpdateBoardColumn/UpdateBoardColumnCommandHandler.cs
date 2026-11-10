using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

/// <summary>
/// Another name for a column. Whether a task is finished comes from Gata, the Done column, which never
/// changes its kind (SPEC section 4.4), so a rename leaves the tasks alone.
/// </summary>
public sealed class UpdateBoardColumnCommandHandler(ProjectChanges changes) : ICommandHandler<UpdateBoardColumnCommand, BoardColumnDto>
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

        var saved = await changes.Save(ct);
        return saved.IsFailure ? saved.Error : project.Columns.Single(column => column.Id == command.ColumnId).ToDto();
    }
}
