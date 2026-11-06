using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Tasks.Commands.ArchiveTask;

/// <summary>
/// DELETE archives: the row, its history, comments and attachments stay, and the reports still read
/// them (SPEC section 5.1). Any real member can archive; seeing and restoring archived tasks takes tasks.archive.
/// </summary>
public sealed class ArchiveTaskCommandHandler(
    TaskChanges changes,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<ArchiveTaskCommandHandler> logger) : ICommandHandler<ArchiveTaskCommand, Unit>
{
    public async Task<Result<Unit>> Handle(ArchiveTaskCommand command, CancellationToken ct)
    {
        var loaded = await changes.LoadOpen(command.TaskId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        loaded.Value.Archive(currentUser.Id, clock.UtcNow);

        var saved = await changes.Save(ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        logger.LogInformation("Task {TaskId} archived", command.TaskId);
        return Unit.Value;
    }
}
