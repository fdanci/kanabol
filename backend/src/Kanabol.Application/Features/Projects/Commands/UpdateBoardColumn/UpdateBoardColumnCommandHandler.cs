using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Domain.Projects;

namespace Kanabol.Application.Features.Projects.Commands.UpdateBoardColumn;

/// <summary>
/// Another name for a column of the project's own: the fixed ones keep theirs (409 Column.Fixed, SPEC
/// section 4.4). Whether a task is finished comes from Gata, so a rename leaves the tasks alone.
/// </summary>
public sealed class UpdateBoardColumnCommandHandler(ProjectChanges changes, ICurrentUser currentUser) : ICommandHandler<UpdateBoardColumnCommand, BoardColumnDto>
{
    public async Task<Result<BoardColumnDto>> Handle(UpdateBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenForColumns(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var project = loaded.Value;
        switch (project.RenameColumn(command.ColumnId, command.Name, currentUser.Id))
        {
            case ColumnChange.NotFound:
                return ColumnErrors.NotFound;
            case ColumnChange.Fixed:
                return ColumnErrors.Fixed;
        }

        var saved = await changes.Save(ct);
        return saved.IsFailure ? saved.Error : project.Columns.Single(column => column.Id == command.ColumnId).ToDto();
    }
}
