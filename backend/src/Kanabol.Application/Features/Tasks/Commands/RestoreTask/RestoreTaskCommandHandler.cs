using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Tasks.Commands.RestoreTask;

/// <summary>
/// tasks.archive is checked on the endpoint, from the token. Seeing the project is enough here, so an
/// admin restores a task without joining its project, but an archived project has to be restored first.
/// </summary>
public sealed class RestoreTaskCommandHandler(
    TaskChanges changes,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<RestoreTaskCommandHandler> logger) : ICommandHandler<RestoreTaskCommand, TaskItemDto>
{
    public async Task<Result<TaskItemDto>> Handle(RestoreTaskCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadForRestore(command.TaskId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var wasArchived = loaded.Value.ArchivedAt is not null;
        loaded.Value.Restore(currentUser.Id, clock.UtcNow);

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        if (wasArchived)
        {
            logger.LogInformation("Task {TaskId} restored", command.TaskId);
        }

        return await changes.Read(command.TaskId, ct);
    }
}
