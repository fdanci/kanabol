using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Projects;
using Kanabol.Application.Features.Tasks;
using Kanabol.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Attachments.Commands.DeleteAttachment;

/// <summary>
/// Deletes a file for good, its row and then the file on disk (SPEC section 4.4): for whoever uploaded it,
/// still a real member, or for tasks.archive, from the token, whoever sees the project; on an open task of
/// an open project, as any change. A file the disk keeps after its row went is logged, not answered.
/// </summary>
public sealed class DeleteAttachmentCommandHandler(
    IAppDbContext db,
    TaskVisibility visibility,
    ICurrentUser currentUser,
    IFileStorage storage,
    ILogger<DeleteAttachmentCommandHandler> logger) : ICommandHandler<DeleteAttachmentCommand, Unit>
{
    public async Task<Result<Unit>> Handle(DeleteAttachmentCommand command, CancellationToken ct)
    {
        var attachment = await db.Attachments.FirstOrDefaultAsync(found => found.Id == command.AttachmentId, ct);
        if (attachment is null)
        {
            return AttachmentErrors.NotFound;
        }

        var seen = await visibility.Check(attachment.TaskId, ct);
        if (seen.IsFailure)
        {
            return seen.Error == TaskErrors.NotFound ? AttachmentErrors.NotFound : seen.Error;
        }

        var refusal = RefusalFor(seen.Value, attachment.UploadedById);
        if (refusal is not null)
        {
            return refusal;
        }

        var task = await db.TaskItems.FirstAsync(found => found.Id == attachment.TaskId, ct);
        task.RemoveAttachment(attachment, currentUser.Id);
        await db.SaveChangesAsync(ct);

        try
        {
            await storage.DeleteAsync(attachment.StorageKey, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Attachment {AttachmentId} was deleted, but its file {StorageKey} stays on disk", attachment.Id, attachment.StorageKey);
        }

        logger.LogInformation("Attachment {AttachmentId} deleted from task {TaskId}", attachment.Id, attachment.TaskId);
        return Unit.Value;
    }

    private Error? RefusalFor(VisibleTask task, Guid uploadedById)
    {
        if (task.ProjectArchived)
        {
            return ProjectErrors.Archived;
        }

        if (task.TaskArchived)
        {
            return TaskErrors.Archived;
        }

        if (currentUser.HasPermission(Permissions.TasksArchive))
        {
            return null;
        }

        if (uploadedById != currentUser.Id)
        {
            return AttachmentErrors.NotYours;
        }

        return task.IsRealMember ? null : ProjectErrors.MemberRequired;
    }
}
