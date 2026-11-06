using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Projects.Commands.ArchiveProject;

/// <summary>Projects are archived, not deleted: their tasks, members and columns stay (SPEC section 5.1).</summary>
public sealed class ArchiveProjectCommandHandler(
    ProjectChanges changes,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ArchiveProjectCommandHandler> logger) : ICommandHandler<ArchiveProjectCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ArchiveProjectCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpenOrArchived(command.Id, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        if (loaded.Value.ArchivedAt is not null)
        {
            return Unit.Value;
        }

        loaded.Value.Archive(currentUser.Id, clock.UtcNow);

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("Project {ProjectId} archived", command.Id);
        return Unit.Value;
    }
}
