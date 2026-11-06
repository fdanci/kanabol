using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.RestoreProject;

public sealed class RestoreProjectCommandHandler(
    IAppDbContext db,
    ProjectChanges changes,
    IProjectAccess projectAccess,
    ICurrentUser currentUser,
    ILogger<RestoreProjectCommandHandler> logger) : ICommandHandler<RestoreProjectCommand, ProjectDto>
{
    public async Task<Result<ProjectDto>> Handle(RestoreProjectCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenOrArchived(command.Id, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (loaded.Value.ArchivedAt is not null)
        {
            loaded.Value.Restore();

            var saved = await changes.Save(ct);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            logger.LogInformation("Project {ProjectId} restored", command.Id);
        }

        return (await db.ReadProject(projectAccess, command.Id, currentUser.Id, ct))!;
    }
}
