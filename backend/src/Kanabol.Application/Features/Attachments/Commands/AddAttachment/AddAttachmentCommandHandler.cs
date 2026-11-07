using Kanabol.Application.Abstractions;
using Kanabol.Application.Abstractions.Messaging;
using Kanabol.Application.Common;
using Kanabol.Application.Features.Tasks;
using Kanabol.Domain.Tasks;
using Microsoft.Extensions.Logging;

namespace Kanabol.Application.Features.Attachments.Commands.AddAttachment;

/// <summary>
/// A file from a real member of an open project, on an open task, as for a comment (SPEC section 4.4). The
/// file goes to IFileStorage first, then its row; a save that fails takes the file away again. The task raises
/// AttachmentsChangedEvent, which its board hears (SPEC section 9).
/// </summary>
public sealed class AddAttachmentCommandHandler(
    IAppDbContext db,
    TaskChanges changes,
    ICurrentUser currentUser,
    IFileStorage storage,
    ILogger<AddAttachmentCommandHandler> logger) : ICommandHandler<AddAttachmentCommand, AttachmentDto>
{
    public async Task<Result<AttachmentDto>> Handle(AddAttachmentCommand command, CancellationToken ct)
    {
        var task = await changes.LoadToAddTo(command.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        var fileName = AttachmentNames.Of(command.FileName);
        var stored = await storage.SaveAsync(command.Content, fileName, ct);
        if (stored.Size > Attachment.MaxSize)
        {
            // The form said less than it sent.
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            return AttachmentErrors.FileRefused("A file goes up to 25 MB.");
        }

        var attachment = task.Value.AddAttachment(fileName, stored.ContentType, stored.Size, stored.Key, currentUser.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await storage.DeleteAsync(stored.Key, CancellationToken.None);
            throw;
        }

        logger.LogInformation(
            "Attachment {AttachmentId} of {Size} bytes added to task {TaskId} as {StorageKey}",
            attachment.Id,
            attachment.Size,
            attachment.TaskId,
            attachment.StorageKey);
        var visible = new VisibleTask(task.Value.Id, task.Value.ProjectId, TaskArchived: false, ProjectArchived: false, IsRealMember: true);
        return (await db.ReadAttachments(visible, currentUser, ct, attachment.Id)).Single();
    }
}
