using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.AddBoardColumn;

/// <summary>
/// The board is the project's, so its owners change it, or projects.manage, as with the project's settings, or
/// a real member whose role has columns.manage (group 23). A new column is of the project's own, right before Gata (SPEC section 4.4). A change to the columns saves
/// the project too, so two of them at once can't both take the same place.
/// </summary>
public sealed class AddBoardColumnCommandHandler(
    ProjectChanges changes,
    ILogger<AddBoardColumnCommandHandler> logger) : ICommandHandler<AddBoardColumnCommand, BoardColumnDto>
{
    public async Task<Result<BoardColumnDto>> Handle(AddBoardColumnCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenForColumns(command.ProjectId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var column = loaded.Value.AddColumn(command.Name);

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("Column {ColumnId} added to project {ProjectId}", column.Id, command.ProjectId);
        return column.ToDto();
    }
}
